using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Telemetry;
using _42.Platform.Cli.UnitTests.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Telemetry;

// The telemetry InitializeHost builds: off without a working configuration or a stored id, otherwise a real PostHog
// client that sends one batch to the EU cloud.
public class CommandTelemetryTests
{
    private const string Token = "phc_test-token";

    private static readonly string Directory = P(@"C:\Users\ada\.42for.net");

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));
    private readonly ScriptedHttpHandler _http = new();
    private readonly List<string[]> _notices = [];

    [Fact]
    public void TelemetryOff_IsNoTelemetry_WithoutAnIdOrANotice()
    {
        var telemetry = Create(Settings(new Dictionary<string, string> { [TelemetrySettings.DO_NOT_TRACK] = "1" }));

        telemetry.ShouldBeOfType<NoCommandTelemetry>().InstallationId.ShouldBeNull();
        _fileSystem.AllFiles.ShouldBeEmpty();
        _notices.ShouldBeEmpty();
    }

    [Fact]
    public void AnIdThatCannotBeStored_IsNoTelemetry_WithoutANotice()
    {
        var fileSystem = new Mock<IFileSystem> { DefaultValue = DefaultValue.Mock };
        fileSystem.Setup(system => system.Path).Returns(_fileSystem.Path);
        fileSystem.Setup(system => system.File.Exists(It.IsAny<string>())).Returns(false);
        fileSystem.Setup(system => system.Directory.CreateDirectory(It.IsAny<string>())).Throws(new UnauthorizedAccessException("Access denied."));

        var telemetry = Create(Settings(), new InstallationIdStore(fileSystem.Object, Directory));

        telemetry.ShouldBeOfType<NoCommandTelemetry>();
        _notices.ShouldBeEmpty();
    }

    [Fact]
    public void TheNotice_IsWrittenOnlyWhenTheIdIsCreated()
    {
        var first = Create(Settings());
        var second = Create(Settings());

        first.InstallationId.ShouldNotBeNull();
        second.InstallationId.ShouldBe(first.InstallationId);
        _notices.ShouldHaveSingleItem().ShouldBe(
        [
            "sform sends anonymous usage data (the command, its outcome and duration) to PostHog.",
            "Set DO_NOT_TRACK=1 to turn it off: https://42for.net/cli/sform#telemetry",
        ]);
    }

    [Fact]
    public async Task ARun_IsSentAsOneBatchToTheEuCloud()
    {
        _http.Respond(HttpStatusCode.OK, """{"status":1}""");
        var telemetry = Create(Settings());

        await telemetry.SendAsync(new CommandRun { Command = "sform storyteller kv get" }, ExitCodes.SUCCESS);

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsoluteUri.ShouldBe("https://eu.i.posthog.com/batch");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain($"\"api_key\":\"{Token}\"");
        request.Body.ShouldContain("\"event\":\"cli_command_executed\"");
        request.Body.ShouldContain($"\"distinct_id\":\"{telemetry.InstallationId}\"");
        request.Body.ShouldContain("\"command\":\"sform storyteller kv get\"");
        request.Body.ShouldContain("\"$process_person_profile\":false");
        request.Body.ShouldContain("\"$geoip_disable\":true");
        request.Body.ShouldNotContain("$is_server");
    }

    // MaxRetries is 0: an unreachable host costs one attempt, and the run goes on.
    [Fact]
    public async Task AnUnreachableHost_IsTriedOnce_AndDropsTheEvent()
    {
        _http.Fail(new HttpRequestException("No such host is known."));
        var telemetry = Create(Settings());

        await Should.NotThrowAsync(() => telemetry.SendAsync(new CommandRun { Command = "sform" }, ExitCodes.SUCCESS));

        _http.Requests.ShouldHaveSingleItem();
    }

    private static string P(string path) => MockUnixSupport.Path(path);

    private static TelemetrySettings Settings(Dictionary<string, string>? environment = null)
    {
        return TelemetrySettings.Resolve(
            new TelemetryOptions { PostHogProjectToken = Token, PostHogHost = "https://eu.i.posthog.com" },
            name => environment?.GetValueOrDefault(name));
    }

    // Without compression: the scripted handler records the body as text.
    private ICommandTelemetry Create(TelemetrySettings settings, InstallationIdStore? store = null)
    {
        return CommandTelemetry.Create(
            settings,
            store ?? new InstallationIdStore(_fileSystem, Directory),
            "Production",
            isMachineSignIn: false,
            NullLogger.Instance,
            new StubHttpClientFactory(_http),
            _notices.Add,
            enableCompression: false);
    }
}
