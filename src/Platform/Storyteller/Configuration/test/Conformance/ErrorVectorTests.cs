using _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Conformance;

// conformance/errors.json against the real provider: every answer maps to the same failure at the startup of a required
// source and at a refresh, which keeps the previous document.
public sealed class ErrorVectorTests : IDisposable
{
    private const string File = "errors.json";

    private readonly EngineFixture _fixture = new();

    public ErrorVectorTests()
    {
        _fixture.Environment["STORYTELLER_KEY"] = ConformanceFiles.Read(File).Value<string>("key");
    }

    public static TheoryData<string> Names()
    {
        return ConformanceFiles.Names(File);
    }

    [Fact]
    public void TheSuite_HasOneErrorsFile()
    {
        ConformanceFiles.OfKind("errors").ShouldBe([File]);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void AtStartup(string name)
    {
        var vector = ConformanceFiles.Load(File)[name];
        var mode = VectorResponses.ModeOf(vector);
        _fixture.Http.Otherwise = VectorResponses.Respond(vector["response"]!);

        var builder = new ConfigurationBuilder();
        _fixture.Add(builder, options => options.Mode = mode);

        var exception = Should.Throw<StorytellerConfigurationException>(() => _fixture.Track(builder.Build()));

        exception.ShouldBeFailure(vector["error"]!);
        exception.Key.ShouldBe(_fixture.Environment["STORYTELLER_KEY"]);
        _fixture.Http.Requests.ShouldNotBeEmpty();

        foreach (var request in _fixture.Http.Requests)
        {
            request.ShouldGoToTheModePath(mode);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task AtRefresh(string name)
    {
        var vector = ConformanceFiles.Load(File)[name];
        var mode = VectorResponses.ModeOf(vector);
        _fixture.RespondDocument("one", EngineFixture.Tag(1));
        var (root, engine) = _fixture.Build(options =>
        {
            options.Mode = mode;
            options.RefreshInterval = TimeSpan.Zero;
        });
        var keys = VectorResponses.KeysOf(root);
        var requests = _fixture.Http.Requests.Count;
        _fixture.Http.Otherwise = VectorResponses.Respond(vector["response"]!);

        (await engine.TryRefreshAsync().WaitAsync(EngineFixture.Patience)).ShouldBeFalse();

        engine.Status.LastError.ShouldNotBeNull().ShouldBeFailure(vector["error"]!);
        VectorResponses.KeysOf(root).ShouldBe(keys, ignoreOrder: true);
        _fixture.Http.Requests.Skip(requests).ShouldNotBeEmpty();

        foreach (var request in _fixture.Http.Requests.Skip(requests))
        {
            request.ShouldGoToTheModePath(mode);
        }
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
