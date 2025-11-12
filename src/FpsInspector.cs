using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using PresentMonFps.ETW;
using PresentMonFps.Natives;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PresentMonFps;

public class FpsInspector
{
    public const string SessionName = "PresentMon-FpsInspector";
    public const string Present = "Present";
    public static bool IsAvailable => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static uint GetProcessIdByName(string processName)
    {
        return Kernel32.GetProcessIdByName(processName);
    }

    public static nint GetMainWindowHandle(uint processId)
    {
        nint mainWindowHandle = IntPtr.Zero;

        Process? p = Process.GetProcesses().Where(p => p.Id == processId).FirstOrDefault();

        if (p != null)
        {
            _ = User32.EnumWindows((hWnd, lParam) =>
            {
                _ = User32.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                if (windowProcessId == processId && User32.IsWindowVisible(hWnd))
                {
                    mainWindowHandle = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
        return mainWindowHandle;
    }

    public static nint GetProcessHandle(uint processId)
    {
        try
        {
            nint processHandle = IntPtr.Zero;

            Process? p = Process.GetProcesses().Where(p => p.Id == processId).FirstOrDefault();

            if (p != null)
            {
                return p.Handle;
            }

            return processHandle;
        }
        catch (Exception e)
        {
            _ = e.Message;
        }

        return IntPtr.Zero;
    }

    public static async Task<uint> GetProcessIdByNameAsync(string processName)
    {
        return await Task.Run(() => Kernel32.GetProcessIdByName(processName));
    }

    public static bool IsRunAsAdmin()
    {
        return AdvApi32.IsRunAsAdmin();
    }

    public static bool IsRunAsAdmin(nint hWnd)
    {
        return AdvApi32.IsRunAsAdmin(hWnd);
    }

    public static async Task<FpsResult> StartOnceAsync(FpsRequest request)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw new FpsInspectorException($"For now only Windows is supported, detected platform is {Environment.OSVersion.Platform}.");
        }

        if (request.TargetPid == 0)
        {
            throw new FpsInspectorException($"Target Pid {nameof(FpsRequest.TargetPid)} is not supported.");
        }

        try
        {
            TaskCompletionSource<FpsResult> tcs = new();
            FpsResult result = new();
            FpsCalculator fps = new();
            int pid = (int)request.TargetPid;
            TraceEventID presentEventId = (TraceEventID)Microsoft_Windows_DxgKrnl.Present_Info.Id;

            await Task.Run(() =>
            {
                using TraceEventSession session = new(SessionName);

                fps.FpsReceived += OnFpsReceived;
                fps.OnePercentLowFpsReceived += OnOnePercentLowFpsReceived;
                fps.FrameTimeReceived += OnFrameTimeReceived;

                session.Source.Dynamic.All += OnDynamicAll;
                session.EnableProvider(Microsoft_Windows_DxgKrnl.GUID);

                _ = Task.Run(() =>
                {
                    session.Source.Process();
                });

                SpinWait.SpinUntil(() =>
                {
                    Thread.Sleep(request.PeriodMillisecond);
                    return fps.Fps != 0d && fps.OnePercentLowFps != 0d && fps.FrameTime != 0d;
                }, 10000);

                fps.FpsReceived -= OnFpsReceived;
                fps.OnePercentLowFpsReceived -= OnOnePercentLowFpsReceived;
                fps.FrameTimeReceived -= OnFrameTimeReceived;
                session.Source.Dynamic.All -= OnDynamicAll;
                session.Source.StopProcessing();

                result.Fps = fps.Fps;
                result.OnePercentLowFps = fps.OnePercentLowFps;
                result.FrameTime = fps.FrameTime;
                tcs.SetResult(result);
            });

            return await tcs.Task;

            void OnDynamicAll(TraceEvent data)
            {
                if (data.ProcessID != pid)
                {
                    return;
                }

                if (data.ProviderGuid == Microsoft_Windows_DxgKrnl.GUID)
                {
                    if (data.ID == presentEventId)
                    {
                        DateTime timestamp = data.TimeStamp;
                        fps.Calculate(timestamp.Ticks);
                    }
                }
            }

            void OnFpsReceived(double fpsValue)
            {
                result.Fps = fpsValue;
            }

            void OnOnePercentLowFpsReceived(double onePercentLowFpsValue)
            {
                result.OnePercentLowFps = onePercentLowFpsValue;
            }

            void OnFrameTimeReceived(double frameTimeValue)
            {
                result.FrameTime = frameTimeValue;
            }
        }
        catch (Exception e)
        {
            throw new FpsInspectorException(e.Message);
        }
    }

    public static async Task StartForeverAsync(FpsRequest request, Action<FpsResult>? callback = null, CancellationToken? token = null)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw new FpsInspectorException($"For now only Windows is supported, detected platform is {Environment.OSVersion.Platform}.");
        }

        if (request.TargetPid == 0)
        {
            throw new FpsInspectorException($"Target Pid {nameof(FpsRequest.TargetPid)} is not supported.");
        }

        try
        {
            FpsResult result = new();
            FpsCalculator fps = new();
            int pid = (int)request.TargetPid;
            TraceEventID presentEventId = (TraceEventID)Microsoft_Windows_DxgKrnl.Present_Info.Id;

            using TraceEventSession session = new(SessionName);

            fps.FpsReceived += OnFpsReceived;
            fps.OnePercentLowFpsReceived += OnOnePercentLowFpsReceived;
            fps.FrameTimeReceived += OnFrameTimeReceived;

            session.Source.Dynamic.All += OnDynamicAll;
            session.EnableProvider(Microsoft_Windows_DxgKrnl.GUID);

            Task processTask = Task.Factory.StartNew(session.Source.Process, TaskCreationOptions.LongRunning);
            Task consumeTask = Task.Run(() =>
            {
                while (!(token?.IsCancellationRequested ?? false))
                {
                    Thread.Sleep(request.PeriodMillisecond);
                    if (result.IsCanceled)
                    {
                        break;
                    }
                }
            });

            _ = await Task.WhenAny(processTask, consumeTask);

            fps.FpsReceived -= OnFpsReceived;
            fps.OnePercentLowFpsReceived -= OnOnePercentLowFpsReceived;
            fps.FrameTimeReceived -= OnFrameTimeReceived;
            session.Source.Dynamic.All -= OnDynamicAll;
            session.Source.StopProcessing();

            return;

            void OnDynamicAll(TraceEvent data)
            {
                if (data.ProcessID != pid)
                {
                    return;
                }

                if (data.ProviderGuid == Microsoft_Windows_DxgKrnl.GUID)
                {
                    if (data.ID == presentEventId)
                    {
                        DateTime timestamp = data.TimeStamp;
                        fps.Calculate(timestamp.Ticks);
                    }
                }
            }

            void OnFpsReceived(double fpsValue)
            {
                result.Fps = fpsValue;
                if (result.OnePercentLowFps != 0d && result.FrameTime != 0d)
                {
                    callback?.Invoke(result);
                }
            }

            void OnOnePercentLowFpsReceived(double onePercentLowFpsValue)
            {
                result.OnePercentLowFps = onePercentLowFpsValue;
                if (result.Fps != 0d && result.FrameTime != 0d)
                {
                    callback?.Invoke(result);
                }
            }

            void OnFrameTimeReceived(double frameTimeValue)
            {
                result.FrameTime = frameTimeValue;
                if (result.Fps != 0d && result.OnePercentLowFps != 0d)
                {
                    callback?.Invoke(result);
                }
            }
        }
        catch (Exception e)
        {
            throw new FpsInspectorException(e.Message);
        }
    }
}

public sealed class FpsRequest(uint targetPid)
{
    public uint TargetPid { get; set; } = targetPid;
    public int PeriodMillisecond { get; set; } = 100;

    public FpsRequest() : this(default)
    {
    }
}

[DebuggerDisplay("{ToString()}")]
public sealed class FpsResult(double fps, double onePercentLowFps, double frameTime)
{
    /// <summary>
    /// Only used for <see cref="FpsInspector.StartForeverAsync"/>.
    /// </summary>
    public bool IsCanceled { get; set; } = false;

    public double Fps { get; set; } = fps;
    public double OnePercentLowFps { get; set; } = onePercentLowFps;
    public double FrameTime { get; set; } = frameTime;

    public FpsResult() : this(default, default, default)
    {
    }

    public override string ToString() => $"FPS: {Fps}, 1% Low: {OnePercentLowFps}, Frame Time: {FrameTime:F1}ms";
}