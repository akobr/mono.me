using System.Net;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;

public sealed class LifecycleTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly CountingTimeProvider _time = new();
    private readonly EngineFixture _fixture;

    public LifecycleTests()
    {
        _fixture = new EngineFixture(_time);
    }

    [Fact]
    public async Task DisposingDuringAnInFlightPoll_EndsItSilently_AndLeavesNoTimerThatFiresAfterwards()
    {
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build();
        var gate = new GatedResponse(() => EngineFixture.Document("two", EngineFixture.Tag(2), version: 4));
        _fixture.Http.Respond(gate.RespondAsync);
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(EngineFixture.Document("late", EngineFixture.Tag(3)));

        _time.Advance(Interval);
        var poll = engine.TimerRun;
        await gate.Entered;

        ((IDisposable)root).Dispose();
        await poll.WaitAsync(EngineFixture.Patience);

        engine.IsRunning.ShouldBeFalse();
        _time.Active.ShouldBe(0);
        var status = engine.Status;
        status.ConsecutiveFailures.ShouldBe(0);
        status.LastError.ShouldBeNull();
        status.ETag.ShouldBe(EngineFixture.Tag(1));

        _time.Advance(TimeSpan.FromHours(1));

        _fixture.Http.Requests.Count.ShouldBe(2);
        (await engine.TryRefreshAsync()).ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => engine.SwitchKeyAsync("sbt.contoso"));
        _fixture.Http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DetachAttachAndDetach_LeaveASingleTimer()
    {
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));

        var first = builder.Build();
        _time.Active.ShouldBe(1);

        ((IDisposable)first).Dispose();
        _time.Active.ShouldBe(0);
        engine.IsRunning.ShouldBeFalse();

        // A restart publishes the current snapshot without a request.
        var second = builder.Build();
        _time.Active.ShouldBe(1);
        second["App:Name"].ShouldBe("one");

        ((IDisposable)second).Dispose();
        _time.Active.ShouldBe(0);

        var third = _fixture.Track(builder.Build());
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        await _fixture.AdvanceAsync(engine, Interval);

        _fixture.Http.Requests.Count.ShouldBe(2);
        _time.Active.ShouldBe(1);
        _time.MaxActive.ShouldBe(1);
        third["App:Name"].ShouldBe("one");
    }

    [Fact]
    public void DisposingOneOfTwoRoots_KeepsTheEngineRunning()
    {
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var first = builder.Build();
        var second = _fixture.Track(builder.Build());

        ((IDisposable)first).Dispose();
        ((IDisposable)first).Dispose();

        engine.IsRunning.ShouldBeTrue();
        _time.Active.ShouldBe(1);
        second["App:Name"].ShouldBe("one");
    }

    [Fact]
    public async Task AFailedStartup_LeavesNoProviderAttached_SoTheEngineStopsWithTheLastRoot()
    {
        var builder = new ConfigurationBuilder();
        var engine = _fixture.Add(builder);
        _fixture.Http.Respond(HttpStatusCode.ServiceUnavailable);

        Should.Throw<StorytellerConfigurationException>(() => builder.Build());

        engine.IsRunning.ShouldBeFalse();

        // The retry the startup rules expect; then its root goes, the only one ever returned.
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var root = builder.Build();
        root["App:Name"].ShouldBe("one");
        engine.IsRunning.ShouldBeTrue();

        ((IDisposable)root).Dispose();

        engine.IsRunning.ShouldBeFalse();
        _time.Active.ShouldBe(0);
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(EngineFixture.NotModified(EngineFixture.Tag(1)));
        _time.Advance(Interval * 2);
        (await engine.TryRefreshAsync()).ShouldBeFalse();
        _fixture.Http.Requests.Count.ShouldBe(2);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
