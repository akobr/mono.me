using System;
using Json.JsonE;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

/// <summary>
/// Records what the interpreter reports and optionally stops it.
/// </summary>
internal sealed class CountingMeter : IEvaluationMeter
{
    private readonly long _maxTicks;
    private readonly int _maxDepth;
    private readonly long _maxReservedBytes;

    public CountingMeter(long maxTicks = long.MaxValue, int maxDepth = int.MaxValue, long maxReservedBytes = long.MaxValue)
    {
        _maxTicks = maxTicks;
        _maxDepth = maxDepth;
        _maxReservedBytes = maxReservedBytes;
    }

    public long Ticks { get; private set; }

    public int Depth { get; private set; }

    public int MaxObservedDepth { get; private set; }

    public long Frames { get; private set; }

    public long MaxReservedBytes { get; private set; }

    public void Tick(int weight)
    {
        Ticks += weight;
        if (Ticks > _maxTicks)
        {
            throw new MeterStopException("ticks");
        }
    }

    public void EnterFrame()
    {
        if (Depth >= _maxDepth)
        {
            throw new MeterStopException("depth");
        }

        Depth++;
        Frames++;
        MaxObservedDepth = Math.Max(MaxObservedDepth, Depth);
    }

    public void ExitFrame()
    {
        Depth--;
    }

    public void Reserve(long bytes)
    {
        MaxReservedBytes = Math.Max(MaxReservedBytes, bytes);
        if (bytes > _maxReservedBytes)
        {
            throw new MeterStopException("reserve");
        }
    }
}
