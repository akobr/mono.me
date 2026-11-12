namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The time of a throttle, set by the test.
internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
