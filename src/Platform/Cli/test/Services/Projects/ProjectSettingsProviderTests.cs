using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Cli.UnitTests.Output;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.UnitTests.Services.Projects;

// The settings of the context project for the console output (spec section 7.3): read once per run, kept in
// project-settings.cache.json per API and project, used without a request for ten minutes, then asked again with
// If-None-Match; never read without a console or when switched off; any failure is the built-in display.
public class ProjectSettingsProviderTests
{
    private const string BaseUrl = "https://api.example.test/api";
    private const string Organization = AnnotationCommandFixture.Organization;
    private const string Project = AnnotationCommandFixture.Project;
    private const string ProjectKey = $"{Organization}.{Project}";

    private static readonly string CachePath = MockUnixSupport.Path(@"C:\home\project-settings.cache.json");

    private readonly MockFileSystem _fileSystem = new();
    private readonly Mock<IAccessApiClient> _api = new(MockBehavior.Strict);
    private readonly Mock<ICommandContext> _context = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger _logger = new();
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal) { ["WT_SESSION"] = "1" };
    private readonly OutputOptions _options = new();
    private bool _hasConsole = true;
    private Encoding? _encoding = new UTF8Encoding(false);
    private bool _takesEscapeCodes = true;

    public ProjectSettingsProviderTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns(Organization);
        _context.SetupGet(context => context.ProjectName).Returns(Project);
        _context.SetupGet(context => context.IsProjectKnown).Returns(true);
    }

    public static TheoryData<Exception> Failures => new()
    {
        AnnotationCommandFixture.Error(401),
        AnnotationCommandFixture.MissingScope("Annotation.Read"),
        AnnotationCommandFixture.Error(404),
        AnnotationCommandFixture.Error(500),
        new HttpRequestException("No such host is known."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."),
        new AuthenticationException(AuthenticationFailureReason.Configuration, "Sign in first."),
    };

    [Fact]
    public async Task TheFirstRead_AsksTheApi_AndKeepsTheAnswer()
    {
        var reads = SetupRead(null, Ok(Settings(3)));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Company");
        display.DateFormat.ShouldBe("dd.mm.yyyy");
        reads.ShouldBe([null]);
        var entry = ReadCache().Find(BaseUrl, ProjectKey).ShouldNotBeNull();
        entry.ETag.ShouldBe("\"3\"");
        entry.FetchedAt.ShouldBe(_time.Now);
        entry.Settings.Version.ShouldBe(3);
        entry.Settings.Types!["sbt"].Label.ShouldBe("Company");
        _logger.Entries.ShouldBeEmpty();
    }

    // Within ten minutes of a read the cache is used as it is.
    [Fact]
    public async Task AFreshCache_IsUsedWithoutARequest()
    {
        SetupRead(null, Ok(Settings(3)));
        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);
        _api.Invocations.Clear();
        _time.Now += TimeSpan.FromMinutes(9);

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Company");
        _api.Invocations.ShouldBeEmpty();
    }

    // After ten minutes the API is asked with the ETag; a 304 keeps the cache and its time moves on.
    [Fact]
    public async Task AnOldCache_IsRevalidated_AndA304KeepsIt()
    {
        SetupRead(null, Ok(Settings(3)));
        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);
        _time.Now += TimeSpan.FromMinutes(10);
        var reads = SetupRead("\"3\"", NotModified(3));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        reads.ShouldBe(["\"3\""]);
        display.LabelOf("sbt").ShouldBe("Company");
        ReadCache().Find(BaseUrl, ProjectKey)!.FetchedAt.ShouldBe(_time.Now);
    }

    // Offline after a read: the cached settings stay on screen, and the failure is remembered for a minute.
    [Fact]
    public async Task AFailedRevalidation_KeepsTheCachedSettings_AndWaitsAMinuteBeforeAskingAgain()
    {
        SetupRead(null, Ok(Settings(3)));
        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);
        _time.Now += TimeSpan.FromMinutes(11);
        _api.Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, "\"3\"", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No such host is known."));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Company");
        var entry = ReadCache().Find(BaseUrl, ProjectKey).ShouldNotBeNull();
        entry.FailedAt.ShouldBe(_time.Now);
        entry.ETag.ShouldBe("\"3\"");

        _api.Invocations.Clear();
        _time.Now += TimeSpan.FromSeconds(50);
        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).LabelOf("sbt").ShouldBe("Company");
        _api.Invocations.ShouldBeEmpty();

        _time.Now += TimeSpan.FromSeconds(20);
        var reads = SetupRead("\"3\"", NotModified(3));
        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).LabelOf("sbt").ShouldBe("Company");
        reads.ShouldBe(["\"3\""]);
        ReadCache().Find(BaseUrl, ProjectKey)!.FailedAt.ShouldBeNull();
    }

    // A refusal (here a lost access) shows the built-in names, not what was cached before.
    [Fact]
    public async Task ARefusedRevalidation_ShowsTheBuiltInDisplay()
    {
        SetupRead(null, Ok(Settings(3)));
        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);
        _time.Now += TimeSpan.FromMinutes(11);
        _api.Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, "\"3\"", It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(403));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.ShouldBeSameAs(ProjectDisplay.BuiltIn);
        ReadCache().Find(BaseUrl, ProjectKey)!.HasSettings.ShouldBeFalse();
    }

    // Without a cache, a failure is remembered too: the next command within a minute makes no request.
    [Fact]
    public async Task AFailedFirstRead_IsNotRepeatedWithinAMinute()
    {
        _api.Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));
        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(ProjectDisplay.BuiltIn);
        _api.Invocations.Clear();
        _time.Now += TimeSpan.FromSeconds(30);

        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(ProjectDisplay.BuiltIn);

        _api.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnOldCache_IsReplaced_ByNewSettings()
    {
        SetupRead(null, Ok(Settings(3)));
        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);
        _time.Now += TimeSpan.FromHours(2);
        var changed = Settings(4);
        changed.Types["sbt"].Label = "Client";
        SetupRead("\"3\"", Ok(changed));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Client");
        ReadCache().Find(BaseUrl, ProjectKey)!.ETag.ShouldBe("\"4\"");
    }

    // A clock that went back does not keep a cache forever.
    [Fact]
    public async Task ACacheFromTheFuture_IsRevalidated()
    {
        SetupRead(null, Ok(Settings(3)));
        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);
        _time.Now -= TimeSpan.FromDays(1);
        var reads = SetupRead("\"3\"", NotModified(3));

        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        reads.ShouldBe(["\"3\""]);
    }

    // One read per run, however many outputs ask.
    [Fact]
    public async Task TheDisplay_IsReadOncePerRun()
    {
        var reads = SetupRead(null, Ok(Settings(3)));
        var provider = CreateProvider();

        await provider.GetDisplayAsync(TestContext.Current.CancellationToken);
        await provider.GetDisplayAsync(TestContext.Current.CancellationToken);

        reads.Count.ShouldBe(1);
    }

    // The entry of another API or another project is not this project's.
    [Fact]
    public async Task TheCache_IsKeyedByTheApiAndTheProject()
    {
        WriteCache(new ProjectSettingsCacheFile
        {
            Entries =
            [
                Entry("https://other.example.test/api", ProjectKey),
                Entry(BaseUrl, "org1.other"),
            ],
        });
        var reads = SetupRead(null, Ok(SdkDefaults()));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        reads.ShouldBe([null]);
        display.IsBuiltIn.ShouldBeTrue();
        ReadCache().Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task TheCache_MatchesTheApiWithoutItsTrailingSlash()
    {
        WriteCache(new ProjectSettingsCacheFile { Entries = [Entry(BaseUrl + "/", ProjectKey)] });

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Company");
        _api.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABrokenCacheFile_IsReadAgainFromTheApi()
    {
        _fileSystem.AddFile(CachePath, new MockFileData("{ not json"));
        var reads = SetupRead(null, Ok(Settings(3)));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        reads.ShouldBe([null]);
        display.LabelOf("sbt").ShouldBe("Company");
        ReadCache().Find(BaseUrl, ProjectKey).ShouldNotBeNull();
    }

    // The cache is best effort: settings that cannot be kept are still shown.
    [Fact]
    public async Task ACacheThatCannotBeWritten_StillShowsTheSettings()
    {
        _fileSystem.AddFile(MockUnixSupport.Path(@"C:\home"), new MockFileData("a file where the folder should be"));
        SetupRead(null, Ok(Settings(3)));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Company");
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug);
    }

    // Offline, signed out, a machine without a read scope, an older API: the built-in names and one debug line; the
    // failure is remembered without settings.
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AnyFailure_IsTheBuiltInDisplay_WithOneDebugLine(Exception failure)
    {
        _api.Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, null, It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.ShouldBeSameAs(ProjectDisplay.BuiltIn);
        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
        var entry = ReadCache().Find(BaseUrl, ProjectKey).ShouldNotBeNull();
        entry.HasSettings.ShouldBeFalse();
        entry.FailedAt.ShouldBe(_time.Now);
    }

    [Fact]
    public async Task WithoutAConsole_NothingIsAsked()
    {
        _hasConsole = false;
        WriteCache(new ProjectSettingsCacheFile { Entries = [Entry(BaseUrl, ProjectKey)] });

        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(ProjectDisplay.BuiltIn);
        _api.Invocations.ShouldBeEmpty();
    }

    // A machine without a project knows of no project settings.
    [Fact]
    public async Task WithoutAProject_NothingIsAsked()
    {
        _context.SetupGet(context => context.IsProjectKnown).Returns(false);

        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(ProjectDisplay.BuiltIn);
        _api.Invocations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, null, null)]
    [InlineData(false, "builtin", null)]
    [InlineData(false, "BUILTIN", "project")]
    [InlineData(false, null, "builtin")]
    public async Task SwitchedOff_NothingIsAsked(bool builtInFlag, string? variable, string? configured)
    {
        _environment[SformVariables.PROJECT_SETTINGS] = variable;
        _options.ProjectSettings = configured;
        var provider = CreateProvider(switches => switches.IsBuiltInRequested = builtInFlag);

        (await provider.GetDisplayAsync(TestContext.Current.CancellationToken)).ShouldBeSameAs(ProjectDisplay.BuiltIn);
        _api.Invocations.ShouldBeEmpty();
    }

    // SFORM_PROJECT_SETTINGS=project wins over output.projectSettings=builtin.
    [Fact]
    public async Task TheVariable_WinsOverThePreference()
    {
        _environment[SformVariables.PROJECT_SETTINGS] = "project";
        _options.ProjectSettings = "builtin";
        SetupRead(null, Ok(Settings(3)));

        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).LabelOf("sbt").ShouldBe("Company");
    }

    // What sform project settings set, edit and reset answered: shown by the next command without a request.
    [Fact]
    public async Task RememberedSettings_AreUsedWithoutARequest()
    {
        var provider = CreateProvider();

        await provider.RememberAsync(Organization, Project, Settings(8), "\"8\"", TestContext.Current.CancellationToken);
        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.LabelOf("sbt").ShouldBe("Company");
        _api.Invocations.ShouldBeEmpty();
        ReadCache().Find(BaseUrl, ProjectKey)!.ETag.ShouldBe("\"8\"");
    }

    // A write in the same run replaces what the run read before.
    [Fact]
    public async Task RememberedSettings_ReplaceWhatTheRunRead()
    {
        SetupRead(null, Ok(SdkDefaults()));
        var provider = CreateProvider();
        (await provider.GetDisplayAsync(TestContext.Current.CancellationToken)).IsBuiltIn.ShouldBeTrue();

        await provider.RememberAsync(Organization, Project, Settings(1), null, TestContext.Current.CancellationToken);

        (await provider.GetDisplayAsync(TestContext.Current.CancellationToken)).LabelOf("sbt").ShouldBe("Company");
        ReadCache().Find(BaseUrl, ProjectKey)!.ETag.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Remember_NeverThrows()
    {
        _fileSystem.AddFile(MockUnixSupport.Path(@"C:\home"), new MockFileData("a file where the folder should be"));

        await CreateProvider().RememberAsync(Organization, Project, Settings(8), "\"8\"", TestContext.Current.CancellationToken);

        _logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Debug);
    }

    // Older entries go once the file holds the projects read last.
    [Fact]
    public async Task TheCache_KeepsTheProjectsReadLast()
    {
        var entries = new List<ProjectSettingsCacheEntry>();

        for (var index = 0; index < ProjectSettingsCacheFile.MaxEntries; index++)
        {
            entries.Add(Entry(BaseUrl, $"org1.p{index}"));
        }

        WriteCache(new ProjectSettingsCacheFile { Entries = entries });
        SetupRead(null, Ok(Settings(3)));

        await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        var cache = ReadCache();
        cache.Entries.Count.ShouldBe(ProjectSettingsCacheFile.MaxEntries);
        cache.Entries[0].Project.ShouldBe(ProjectKey);
        cache.Find(BaseUrl, $"org1.p{ProjectSettingsCacheFile.MaxEntries - 1}").ShouldBeNull();
    }

    [Fact]
    public async Task Colors_AreTrueColor_InATerminalThatTakesThem()
    {
        SetupRead(null, Ok(Settings(3)));

        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).Colors.ShouldBe(TypeColorMode.TrueColor);
    }

    [Fact]
    public async Task Colors_AreTheConsoleColors_WithoutTrueColorOrEscapeCodes()
    {
        _takesEscapeCodes = false;
        SetupRead(null, Ok(Settings(3)));
        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).Colors.ShouldBe(TypeColorMode.Console);

        _takesEscapeCodes = true;
        _environment.Remove("WT_SESSION");
        _environment["TERM_PROGRAM"] = "vscode";
        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).Colors.ShouldBe(TypeColorMode.Console);
    }

    [Fact]
    public async Task NoColor_TurnsTheTypeColorsOff()
    {
        _environment["NO_COLOR"] = "1";
        SetupRead(null, Ok(Settings(3)));

        var display = await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken);

        display.Colors.ShouldBe(TypeColorMode.None);
        display.LabelOf("sbt").ShouldBe("Company");
    }

    [Theory]
    [InlineData(null, null, "WT_SESSION", true, true)]
    [InlineData("auto", null, "WT_SESSION", false, false)]
    [InlineData(null, null, "TERM_PROGRAM=tmux", true, false)]
    [InlineData(null, "never", "WT_SESSION", true, false)]
    [InlineData("always", null, "TERM_PROGRAM=tmux", false, true)]
    [InlineData("never", "always", "WT_SESSION", true, false)]
    [InlineData("sometimes", "always", "TERM_PROGRAM=tmux", false, true)]
    public async Task Icons_FollowTheSwitchesAndTheTerminal(string? variable, string? configured, string terminal, bool isUtf8, bool expected)
    {
        _environment.Clear();
        var (name, value) = terminal.Contains('=') ? (terminal.Split('=')[0], terminal.Split('=')[1]) : (terminal, "1");
        _environment[name] = value;
        _environment[SformVariables.ICONS] = variable;
        _options.Icons = configured;
        _encoding = isUtf8 ? new UTF8Encoding(false) : Encoding.Latin1;
        SetupRead(null, Ok(Settings(3)));

        (await CreateProvider().GetDisplayAsync(TestContext.Current.CancellationToken)).ShowsIcons.ShouldBe(expected);
    }

    [Fact]
    public void OutputTerminal_OfARecordingConsole_HasNoConsole()
    {
        var terminal = OutputTerminal.Of(new RecordingConsole().Object);

        terminal.HasConsole().ShouldBeFalse();
    }

    [Fact]
    public void OutputTerminal_OfAnSformConsole_ReadsItsWidthAndEncoding()
    {
        var inner = new ColorRecordingConsole(new UTF8Encoding(false));

        var withConsole = OutputTerminal.Of(new SformConsole(inner, () => 120, _ => null));
        withConsole.HasConsole().ShouldBeTrue();
        TerminalCapabilities.IsUtf8(withConsole.OutputEncoding()).ShouldBeTrue();

        OutputTerminal.Of(new SformConsole(inner, () => null, _ => null)).HasConsole().ShouldBeFalse();
    }

    // sform story -t > tree.txt or | grep from a terminal: standard input or error is still the console, so it has a width,
    // but the output goes to a file or a pipe, which gets no settings and no escape codes.
    [Fact]
    public void OutputTerminal_OfARedirectedOutput_HasNoConsole_AndTakesNoEscapeCodes()
    {
        var inner = new ColorRecordingConsole(new UTF8Encoding(false)) { IsOutputRedirected = true };

        var terminal = OutputTerminal.Of(new SformConsole(inner, () => 120, _ => null));

        terminal.HasConsole().ShouldBeFalse();
        terminal.TakesEscapeCodes().ShouldBeFalse();
    }

    [Fact]
    public async Task ARedirectedOutput_MakesNoRequest_AndShowsTheBuiltInDisplay()
    {
        var inner = new ColorRecordingConsole(new UTF8Encoding(false)) { IsOutputRedirected = true };
        _environment["COLORTERM"] = "truecolor";

        var display = await CreateProvider(terminal: OutputTerminal.Of(new SformConsole(inner, () => 120, _ => null)))
            .GetDisplayAsync(TestContext.Current.CancellationToken);

        display.ShouldBeSameAs(ProjectDisplay.BuiltIn);
        _api.VerifyNoOtherCalls();
        _fileSystem.File.Exists(CachePath).ShouldBeFalse();
    }

    private static SdkSettings Settings(long version)
    {
        return new SdkSettings
        {
            Types = new Dictionary<string, SdkAppearance>
            {
                ["sbt"] = new() { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" },
            },
            DefaultTimeZone = "Europe/Prague",
            DateFormat = "dd.mm.yyyy",
            Version = version,
        };
    }

    private static SdkSettings SdkDefaults()
    {
        return new SdkSettings { Types = new Dictionary<string, SdkAppearance>(), Version = 0 };
    }

    private static ConditionalResponse<SdkSettings> Ok(SdkSettings settings)
    {
        return new ConditionalResponse<SdkSettings>(200, settings, new Dictionary<string, IEnumerable<string>> { ["ETag"] = [$"\"{settings.Version}\""] });
    }

    private static ConditionalResponse<SdkSettings> NotModified(long version)
    {
        return new ConditionalResponse<SdkSettings>(304, null, new Dictionary<string, IEnumerable<string>> { ["ETag"] = [$"\"{version}\""] });
    }

    private ProjectSettingsCacheEntry Entry(string baseUrl, string project)
    {
        return new ProjectSettingsCacheEntry
        {
            BaseUrl = baseUrl,
            Project = project,
            ETag = "\"3\"",
            FetchedAt = _time.Now,
            Settings = ProjectSettingsCacheValues.From(Settings(3)),
        };
    }

    // The If-None-Match of each read.
    private List<string?> SetupRead(string? ifNoneMatch, ConditionalResponse<SdkSettings> answer)
    {
        var reads = new List<string?>();
        _api
            .Setup(api => api.GetProjectSettingsConditionalAsync(Organization, Project, ifNoneMatch, It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, CancellationToken>((_, _, tag, _) => reads.Add(tag))
            .ReturnsAsync(answer);
        return reads;
    }

    private ProjectSettingsCacheFile ReadCache()
    {
        return JsonSerializer.Deserialize<ProjectSettingsCacheFile>(_fileSystem.File.ReadAllText(CachePath))!;
    }

    private void WriteCache(ProjectSettingsCacheFile file)
    {
        _fileSystem.AddFile(CachePath, new MockFileData(JsonSerializer.Serialize(file)));
    }

    private ProjectSettingsProvider CreateProvider(Action<OutputSwitches>? configure = null, OutputTerminal? terminal = null)
    {
        var switches = new OutputSwitches(_options, name => _environment.GetValueOrDefault(name));
        configure?.Invoke(switches);

        return new ProjectSettingsProvider(
            _api.Object,
            _context.Object,
            _fileSystem,
            CachePath,
            BaseUrl,
            switches,
            terminal ?? new OutputTerminal(() => _hasConsole, () => _encoding, () => _takesEscapeCodes),
            _time,
            _logger);
    }

    internal sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
