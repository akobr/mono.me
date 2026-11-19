using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Processes;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The project settings in the console output, with the built sform in a pseudo terminal against a stub of the API whose
// project names its subjects Company (pink, the icon building-2): the terminal shows them. The same command with only its
// standard output redirected to a file (sform … > file typed in the terminal: standard input and error stay the console,
// so sform still has its width) writes the built-in output to the file, byte for byte without colors, symbols or escape
// codes, and makes no request for the settings (spec 7.2 of the project settings).
[Category(CliCategories.SelfTest)]
[Category(CliCategories.Terminal)]
public sealed class ProjectSettingsTerminalSelfTests
{
    private const string Settings = """
        {
          "Types": { "sbt": { "Label": "Company", "Plural": "Companies", "Color": "pink", "Icon": "i-lucide-building-2" } },
          "DateFormat": "dd.mm.yyyy",
          "Version": 1
        }
        """;

    private const string Subjects = """{ "Annotations": [ { "AnnotationType": "Subject", "ProjectName": "display", "ViewName": "default", "AnnotationKey": "sbt.northwind", "Name": "northwind" } ] }""";

    private const string NoAnnotations = """{ "Annotations": [] }""";

    // The row of sbt in the table of project settings: its color, then its icon after a symbol (an emoji, or "?" where
    // the code page of the console has none) or alone.
    private static readonly Regex RowWithSymbol = new(@"pink\s+\S+\s+i-lucide-building-2", RegexOptions.CultureInvariant);
    private static readonly Regex PlainRow = new(@"sbt\s+Subject\s+Company\s+Companies\s+pink\s+i-lucide-building-2", RegexOptions.CultureInvariant);

    // The tree's tag of the subject: the project's name in the terminal, the built-in tag in the redirected file, which
    // gets no escape codes even with COLORTERM=truecolor. The runs have NO_COLOR (SformProcessEnvironment), so the colors
    // themselves are not checked here; ProjectSettingsProviderTests checks how the terminal decides them.
    [Test]
    public async Task StoryTree_InATerminal_ShowsTheProjectsName_ItsRedirectedOutputTheBuiltInTag()
    {
        SkipWithoutTerminal();
        await using var api = Api();
        using var sandbox = new SelfTestSandbox();
        var file = sandbox.FileSystem.Path.Combine(sandbox.WorkingDirectory, "tree.txt");

        // redirected first: no cache of the settings exists yet that would spare a request anyway
        var written = await RunRedirectedAsync(sandbox, Options(api, sandbox, file), file, "story", "-t");
        await Assert.That(written).Contains("Tree of subjects").Because(written);
        await Assert.That(written).Contains("northwind [subject]").Because(written);
        await Assert.That(written).DoesNotContain("[Company]").Because(written);
        await Assert.That(written).DoesNotContain("\u001b").Because("a redirected output gets no escape codes");
        await Assert.That(SettingsReads(api)).IsEqualTo(0).Because(api.Describe());

        var shown = await RunAsync(sandbox, Options(api, sandbox), "story", "-t");
        shown.ShouldContain("Tree of subjects").ShouldContain("[Company]").ShouldNotContain("[subject]");
        await Assert.That(SettingsReads(api)).IsEqualTo(1).Because(api.Describe());
    }

    // sform project settings with SFORM_ICONS=always: the symbol of each icon before its name in the terminal (spec 7.1),
    // and neither symbols nor swatches in the redirected output.
    [Test]
    public async Task ProjectSettings_InATerminal_ShowsTheIconSymbols_ItsRedirectedOutputTheNames()
    {
        SkipWithoutTerminal();
        await using var api = Api();
        using var sandbox = new SelfTestSandbox();
        var file = sandbox.FileSystem.Path.Combine(sandbox.WorkingDirectory, "settings.txt");

        var shown = await RunAsync(sandbox, Options(api, sandbox, icons: "always"), "project", "settings");
        shown.ShouldContain("# Project settings of selftest.display").ShouldMatch(RowWithSymbol);

        var written = await RunRedirectedAsync(sandbox, Options(api, sandbox, file, icons: "always"), file, "project", "settings");
        await Assert.That(written).Contains("# Project settings of selftest.display").Because(written);
        await Assert.That(PlainRow.IsMatch(written)).IsTrue().Because(written);
        await Assert.That(RowWithSymbol.IsMatch(written)).IsFalse().Because(written);
        await Assert.That(written).DoesNotContain("■").Because(written);
        await Assert.That(written).DoesNotContain("\u001b").Because("a redirected output gets no escape codes");
    }

    private static void SkipWithoutTerminal()
    {
        if (TerminalSupport.Reason() is { } reason)
        {
            Skip.Test($"Terminal: {reason}");
        }
    }

    // A run in the pseudo terminal to its end, which must succeed.
    private static async Task<SformResult> RunAsync(SelfTestSandbox sandbox, SformRunOptions options, params string[] arguments)
    {
        await using var terminal = await sandbox.Runner.StartAsync(arguments, options);
        var result = (await terminal.WaitForExitAsync()).ShouldSucceed();
        await Assert.That(result.IsTerminal).IsTrue();
        return result;
    }

    // A run in the pseudo terminal with its standard output in the file, which must succeed; the file's text. A failure
    // shows the file, the terminal and sform's log (where a crash leaves its exception), as the sandbox goes with the test.
    private static async Task<string> RunRedirectedAsync(SelfTestSandbox sandbox, SformRunOptions options, string file, params string[] arguments)
    {
        await using var terminal = await sandbox.Runner.StartAsync(arguments, options);
        var result = await terminal.WaitForExitAsync();
        var written = sandbox.FileSystem.File.Exists(file) ? sandbox.FileSystem.File.ReadAllText(file) : "(no file)";

        if (result.ExitCode != (int)CliExit.Success)
        {
            var logs = sandbox.FileSystem.Path.Combine(sandbox.Home.Path, "logs");
            var log = sandbox.FileSystem.Directory.Exists(logs)
                ? string.Join('\n', sandbox.FileSystem.Directory.GetFiles(logs).Select(sandbox.FileSystem.File.ReadAllText))
                : "(no log)";
            Assert.Fail($"sform {string.Join(' ', arguments)} > {file} ended with {result.ExitCode}; the file:\n{written}\nthe terminal:\n{result.PlainStdOut}\nthe log:\n{log}");
        }

        await Assert.That(result.IsTerminal).IsTrue();
        return written;
    }

    // A run in the pseudo terminal as the machine of the stub, with its standard output in the file when one is given, and
    // with COLORTERM=truecolor, under which a terminal without NO_COLOR gets 24-bit escape codes (a redirected output never).
    private static SformRunOptions Options(StubApi api, SelfTestSandbox sandbox, string? file = null, string? icons = null)
    {
        var options = new SformRunOptions
        {
            Machine = MachineOf(api),
            Home = sandbox.Home,
            Terminal = true,
            StandardOutputFile = file,
            Environment = { ["COLORTERM"] = "truecolor" },
        };

        if (icons is not null)
        {
            options.Environment["SFORM_ICONS"] = icons;
        }

        return options;
    }

    private static int SettingsReads(StubApi api)
    {
        return api.Requests.Count(request => request.Method == "GET" && request.Path.Split('?')[0].EndsWith("/settings", StringComparison.Ordinal));
    }

    // The settings of the project, its subject northwind, and no executions.
    private static StubApi Api()
    {
        return new StubApi(request =>
        {
            var path = request.Path.Split('?')[0];
            if (request.Method != "GET")
            {
                return StubResponse.NotFound;
            }

            if (path.EndsWith("/selftest/display/settings", StringComparison.Ordinal))
            {
                return StubResponse.Json(Settings);
            }

            if (path.EndsWith("/subjects", StringComparison.Ordinal))
            {
                return StubResponse.Json(Subjects);
            }

            return path.EndsWith("/executions", StringComparison.Ordinal) ? StubResponse.Json(NoAnnotations) : StubResponse.NotFound;
        });
    }

    // An API key machine of the organization selftest and its project display, against the stub.
    private static MachineCredentials MachineOf(StubApi api)
    {
        var segment = Convert.ToBase64String(Encoding.UTF8.GetBytes("selftest:display:m-selftest")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return MachineCredentials.ForApiKey($"2s.{segment}.not-a-secret-of-any-target", api.Url);
    }
}
