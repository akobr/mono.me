using _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;
using Microsoft.Extensions.Primitives;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Conformance;

// conformance/refresh.json against the real provider: a start, then one TryRefreshAsync per step, each answered by the
// step's response.
public sealed class RefreshScenarioTests : IDisposable
{
    private const string File = "refresh.json";

    // Short for a read that gets no answer, long enough for a startup on a loaded machine; the system clock runs it.
    private static readonly TimeSpan TimeoutOfAHang = TimeSpan.FromSeconds(1);

    private readonly EngineFixture _fixture = new();

    public RefreshScenarioTests()
    {
        _fixture.Environment["STORYTELLER_KEY"] = ConformanceFiles.Read(File).Value<string>("key");
    }

    public static TheoryData<string> Names()
    {
        return ConformanceFiles.Names(File, "scenarios");
    }

    [Fact]
    public void TheSuite_HasOneRefreshFile()
    {
        ConformanceFiles.OfKind("refresh").ShouldBe([File]);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Scenario(string name)
    {
        var scenario = ConformanceFiles.Load(File, "scenarios")[name];
        var mode = VectorResponses.ModeOf(scenario);
        var steps = scenario["steps"]!.ToList();
        var hangs = steps.Any(step => step.Value<bool?>("timeout") == true);

        _fixture.Http.Otherwise = VectorResponses.Respond(scenario["start"]!["response"]!);
        var (root, engine) = _fixture.Build(options =>
        {
            options.Mode = mode;
            options.RefreshInterval = TimeSpan.Zero;

            // The read timeout bounds the startup too, so only a scenario with a timeout step shortens it.
            if (hangs)
            {
                options.ReadTimeout = TimeoutOfAHang;
                options.Resilience.AttemptTimeout = Timeout.InfiniteTimeSpan;
            }
        });

        var notifications = 0;
        _fixture.Track(ChangeToken.OnChange(root.GetReloadToken, () => Interlocked.Increment(ref notifications)));
        VectorResponses.KeysOf(root).ShouldBe(VectorResponses.ExpectedKeys(scenario["start"]!["keys"]!), ignoreOrder: true);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var expect = step["expect"]!;
            var because = $"step {index + 1}";
            var requests = _fixture.Http.Requests.Count;

            _fixture.Http.Otherwise = step.Value<bool?>("timeout") == true
                ? VectorResponses.NeverAsync
                : VectorResponses.Respond(step["response"]!);

            (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBe(expect.Value<bool>("refreshed"), because);

            VectorResponses.KeysOf(root).ShouldBe(VectorResponses.ExpectedKeys(expect["keys"]!), ignoreOrder: true, because);
            Volatile.Read(ref notifications).ShouldBe(expect.Value<int>("notifications"), because);

            // Every request of the run, also a retry, sends the tag.
            var stepRequests = _fixture.Http.Requests.Skip(requests).ToList();
            stepRequests.ShouldNotBeEmpty(because);

            foreach (var request in stepRequests)
            {
                request.ShouldGoToTheModePath(mode);
                request.Headers.GetValueOrDefault("If-None-Match").ShouldBe(expect.Value<string?>("ifNoneMatch"), because);
            }

            if (expect["error"] is { } error)
            {
                engine.Status.LastError.ShouldNotBeNull(because).ShouldBeFailure(error);
                engine.Status.ConsecutiveFailures.ShouldBePositive(because);
            }
            else
            {
                engine.Status.ConsecutiveFailures.ShouldBe(0, because);
            }
        }
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
