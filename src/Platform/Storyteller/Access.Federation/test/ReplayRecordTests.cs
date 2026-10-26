namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class ReplayRecordTests
{
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void TryAdd_RefusesAKeySeenBefore_UntilItExpires()
    {
        using var record = new ReplayRecord(10, _time);

        record.TryAdd("a", _time.Now.AddMinutes(5)).ShouldBeTrue();
        record.TryAdd("a", _time.Now.AddMinutes(5)).ShouldBeFalse();
        record.TryAdd("b", _time.Now.AddMinutes(5)).ShouldBeTrue();

        _time.Advance(TimeSpan.FromMinutes(5));
        record.TryAdd("a", _time.Now.AddMinutes(5)).ShouldBeTrue();
    }

    // Nothing recorded is evicted to make room: a full record refuses until entries expire.
    [Fact]
    public void TryAdd_WhenFull_Refuses_WithoutForgetting()
    {
        using var record = new ReplayRecord(2, _time);
        record.TryAdd("a", _time.Now.AddMinutes(5)).ShouldBeTrue();
        record.TryAdd("b", _time.Now.AddMinutes(1)).ShouldBeTrue();

        record.TryAdd("c", _time.Now.AddMinutes(5)).ShouldBeFalse();
        record.IsFull.ShouldBeTrue();
        record.TryAdd("a", _time.Now.AddMinutes(5)).ShouldBeFalse();
        record.TryAdd("b", _time.Now.AddMinutes(5)).ShouldBeFalse();

        _time.Advance(TimeSpan.FromMinutes(1));
        record.TryAdd("c", _time.Now.AddMinutes(5)).ShouldBeTrue();
    }

    [Fact]
    public void TryCount_AllowsMaxUsesInAFixedWindow()
    {
        using var record = new ReplayRecord(10, _time);

        record.TryCount("a", TimeSpan.FromMinutes(1), 2).ShouldBeTrue();
        _time.Advance(TimeSpan.FromSeconds(59));
        record.TryCount("a", TimeSpan.FromMinutes(1), 2).ShouldBeTrue();
        record.TryCount("a", TimeSpan.FromMinutes(1), 2).ShouldBeFalse();
        record.TryCount("b", TimeSpan.FromMinutes(1), 2).ShouldBeTrue();

        // The window started with the first use.
        _time.Advance(TimeSpan.FromSeconds(1));
        record.TryCount("a", TimeSpan.FromMinutes(1), 2).ShouldBeTrue();
    }

    [Fact]
    public void TryCount_WhenFull_Refuses()
    {
        using var record = new ReplayRecord(1, _time);
        record.TryCount("a", TimeSpan.FromMinutes(1), 5).ShouldBeTrue();

        record.TryCount("b", TimeSpan.FromMinutes(1), 5).ShouldBeFalse();
        record.TryCount("a", TimeSpan.FromMinutes(1), 5).ShouldBeTrue();
    }
}
