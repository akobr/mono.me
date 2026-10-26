namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// A clock the test moves; timers stay those of the system.
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
