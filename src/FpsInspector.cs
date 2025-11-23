using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using PresentMonFps.Natives;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PresentMonFps;

public class FpsInspector
{
    private static readonly Guid DxgKrnlGuid = new("802ec45a-1e99-4b83-9920-87c98277ba9d");
    private const int PresentEventId = 0x00b8;

    public static bool IsAvailable => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static uint GetProcessIdByName(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        return processes.Length > 0 ? (uint)processes[0].Id : 0;
    }

    public static nint GetMainWindowHandle(uint processId)
    {
        nint mainWindowHandle = IntPtr.Zero;
        User32.EnumWindows((hWnd, lParam) =>
        {
            User32.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
            if (windowProcessId == processId && User32.IsWindowVisible(hWnd))
            {
                mainWindowHandle = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return mainWindowHandle;
    }

    public static nint GetProcessHandle(uint processId)
    {
        try
        {
            using Process p = Process.GetProcessById((int)processId);
            return p.Handle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static Task<uint> GetProcessIdByNameAsync(string processName)
    {
        return Task.Run(() => GetProcessIdByName(processName));
    }

    public static bool IsRunAsAdmin() => AdvApi32.IsRunAsAdmin();

    public static bool IsRunAsAdmin(nint hWnd) => AdvApi32.IsRunAsAdmin(hWnd);

    public static async Task<FpsResult> StartOnceAsync(FpsRequest request)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException("Windows only.");
        if (request.TargetPid == 0) throw new ArgumentException("Invalid TargetPid.");

        TaskCompletionSource<FpsResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FpsResult result = new();
        FpsCalculator fps = new();
        int pid = (int)request.TargetPid;
        string sessionName = $"PresentMon-FpsInspector-{Guid.NewGuid()}";

        using CancellationTokenSource cts = new(request.PeriodMillisecond * 2 + 5000);
        using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                using TraceEventSession session = new(sessionName);

                object lockObj = new();
                bool completed = false;

                void CheckCompletion()
                {
                    lock (lockObj)
                    {
                        if (completed) return;
                        if (result.Fps > 0 && result.OnePercentLowFps > 0 && result.FrameTime > 0)
                        {
                            completed = true;
                            session.Source.StopProcessing();
                            tcs.TrySetResult(result);
                        }
                    }
                }

                fps.FpsReceived += (v) => { result.Fps = v; CheckCompletion(); };
                fps.OnePercentLowFpsReceived += (v) => { result.OnePercentLowFps = v; CheckCompletion(); };
                fps.FrameTimeReceived += (v) => { result.FrameTime = v; CheckCompletion(); };

                session.Source.AllEvents += (data) =>
                {
                    if (data.ProviderGuid == DxgKrnlGuid &&
                        (int)data.ID == PresentEventId &&
                        data.ProcessID == pid)
                    {
                        fps.Calculate(data.TimeStamp.Ticks);
                    }
                };

                session.EnableProvider(DxgKrnlGuid, TraceEventLevel.Informational, 0x1);
                session.Source.Process();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(new FpsInspectorException(ex.Message));
            }
        }, TaskCreationOptions.LongRunning);

        return await tcs.Task.ConfigureAwait(false);
    }

    public static async Task StartForeverAsync(FpsRequest request, Action<FpsResult>? callback = null, CancellationToken? token = null)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException("Windows only.");
        if (request.TargetPid == 0) throw new ArgumentException("Invalid TargetPid.");

        string sessionName = $"PresentMon-FpsInspector-{Guid.NewGuid()}";
        int pid = (int)request.TargetPid;
        FpsResult result = new();
        FpsCalculator fps = new();
        CancellationToken ct = token ?? CancellationToken.None;

        try
        {
            using TraceEventSession session = new(sessionName);

            void TryCallback()
            {
                if (result.Fps > 0 && result.FrameTime > 0 && result.OnePercentLowFps > 0)
                {
                    callback?.Invoke(result);
                }
            }

            fps.FpsReceived += (v) => { result.Fps = v; TryCallback(); };
            fps.OnePercentLowFpsReceived += (v) => { result.OnePercentLowFps = v; TryCallback(); };
            fps.FrameTimeReceived += (v) => { result.FrameTime = v; TryCallback(); };

            session.Source.AllEvents += (data) =>
            {
                if (data.ProviderGuid == DxgKrnlGuid &&
                    (int)data.ID == PresentEventId &&
                    data.ProcessID == pid)
                {
                    fps.Calculate(data.TimeStamp.Ticks);
                }
            };

            session.EnableProvider(DxgKrnlGuid, TraceEventLevel.Informational, 0x1);

            Task processingTask = Task.Factory.StartNew(() =>
            {
                session.Source.Process();
            }, TaskCreationOptions.LongRunning);

            try
            {
                while (!ct.IsCancellationRequested && !result.IsCanceled)
                {
                    await Task.Delay(request.PeriodMillisecond, ct).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                session.Source.StopProcessing();
                await Task.WhenAny(processingTask, Task.Delay(1000)).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            throw new FpsInspectorException(e.Message);
        }
    }
}

public sealed class FpsRequest
{
    public uint TargetPid { get; set; }
    public int PeriodMillisecond { get; set; } = 100;

    public FpsRequest(uint targetPid)
    {
        TargetPid = targetPid;
    }

    public FpsRequest() { }
}

[DebuggerDisplay("{ToString()}")]
public sealed class FpsResult
{
    public bool IsCanceled { get; set; }
    public double Fps { get; set; }
    public double OnePercentLowFps { get; set; }
    public double FrameTime { get; set; }

    public FpsResult(double fps, double onePercentLowFps, double frameTime)
    {
        Fps = fps;
        OnePercentLowFps = onePercentLowFps;
        FrameTime = frameTime;
    }

    public FpsResult() { }

    public override string ToString() => $"FPS: {Fps}, 1% Low: {OnePercentLowFps}, Frame Time: {FrameTime:F1}ms";
}