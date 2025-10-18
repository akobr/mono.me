using System.Diagnostics;

namespace _42.Crumble;

public sealed class PhaseStopwatch
{
    private long _time = Stopwatch.GetTimestamp();

    public double ElapsedMs() => (Stopwatch.GetTimestamp() - _time) * 1000.0 / Stopwatch.Frequency;

    public double SplitMs()
    {
        var now = Stopwatch.GetTimestamp();
        var ms = (now - _time) * 1000.0 / Stopwatch.Frequency;
        _time = now;
        return ms;
    }
}
