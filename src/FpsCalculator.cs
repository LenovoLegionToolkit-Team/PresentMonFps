using System;
using System.Linq;

namespace PresentMonFps;

public sealed class FpsCalculator
{
    private const int _sampleCount = 500;
    private long[] _frameTimes = new long[_sampleCount];
    private int _index = 0;
    private int _count = 0;
    private long _lastTimestampTicks = 0;
    private long _currentFrameTimeTicks = 0;

    public double _fps = default;
    public double _onePercentLowFps = default;
    public double _frameTime = default;

    public double Fps
    {
        get => _fps;
        private set
        {
            if (_fps != value)
            {
                _fps = value;
                FpsReceived?.Invoke(value);
            }
        }
    }

    public double OnePercentLowFps
    {
        get => _onePercentLowFps;
        private set
        {
            if (_onePercentLowFps != value)
            {
                _onePercentLowFps = value;
                OnePercentLowFpsReceived?.Invoke(value);
            }
        }
    }

    public double FrameTime
    {
        get => _frameTime;
        private set
        {
            if (_frameTime != value)
            {
                _frameTime = value;
                FrameTimeReceived?.Invoke(value);
            }
        }
    }

    public event Action<double>? FpsReceived = null;
    public event Action<double>? OnePercentLowFpsReceived = null;
    public event Action<double>? FrameTimeReceived = null;

    public unsafe void Calculate(long timestampTicks)
    {
        if (_lastTimestampTicks != 0)
        {
            _currentFrameTimeTicks = timestampTicks - _lastTimestampTicks;

            double currentFrameTimeMs = _currentFrameTimeTicks / (double)TimeSpan.TicksPerMillisecond;
            FrameTime = currentFrameTimeMs;

            fixed (long* pFrameTimes = _frameTimes)
            {
                pFrameTimes[_index] = _currentFrameTimeTicks;
                _index = (_index + 1) % _sampleCount;
                if (_count < _sampleCount)
                {
                    _count++;
                }
            }

            if (_count >= 2)
            {
                long firstFrameTimeIndex = (_index - _count + _sampleCount) % _sampleCount;

                long totalTimeTicks = 0;
                for (int i = 0; i < _count; i++)
                {
                    totalTimeTicks += _frameTimes[(firstFrameTimeIndex + i) % _sampleCount];
                }

                double totalTimeSeconds = totalTimeTicks / (double)TimeSpan.TicksPerSecond;
                double fps = (_count - 1) / totalTimeSeconds;
                Fps = fps;
            }

            if (_count == _sampleCount)
            {
                CalculateOnePercentLow();
            }
        }
        _lastTimestampTicks = timestampTicks;
    }

    private unsafe void CalculateOnePercentLow()
    {
        long[] frameTimesCopy = new long[_sampleCount];
        Array.Copy(_frameTimes, frameTimesCopy, _sampleCount);

        double[] fpsSamples = new double[_sampleCount];
        for (int i = 0; i < _sampleCount; i++)
        {
            if (frameTimesCopy[i] <= 0) continue;
            double frameTimeInSeconds = frameTimesCopy[i] / (double)TimeSpan.TicksPerSecond;
            fpsSamples[i] = 1.0 / frameTimeInSeconds;
        }

        var validFpsSamples = fpsSamples.Where(fps => fps > 0).ToArray();
        if (validFpsSamples.Length == 0) return;

        Array.Sort(validFpsSamples);

        int onePercentIndex = (int)(validFpsSamples.Length * 0.01);
        if (onePercentIndex < 1) onePercentIndex = 1;

        double[] worstOnePercentFps = new double[onePercentIndex];
        Array.Copy(validFpsSamples, 0, worstOnePercentFps, 0, onePercentIndex);

        double onePercentLowFps = worstOnePercentFps.Average();
        OnePercentLowFps = onePercentLowFps;
    }
}