using System;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock advances by a fixed step on every timestamp read.
/// </summary>
internal sealed class SteppingTimeProvider : TimeProvider
{
    private readonly long _step;
    private long _now;

    public SteppingTimeProvider(TimeSpan step)
    {
        _step = (long)(step.TotalSeconds * TimestampFrequency);
    }

    public override long TimestampFrequency => 1_000_000;

    public override long GetTimestamp()
    {
        _now += _step;
        return _now;
    }
}
