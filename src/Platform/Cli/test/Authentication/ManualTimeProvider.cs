using System;

namespace _42.Platform.Cli.UnitTests.Authentication;

internal sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}
