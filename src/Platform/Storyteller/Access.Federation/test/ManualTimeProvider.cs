namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// A clock the tests move by hand.
internal sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset? now = null)
    {
        Now = now ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
