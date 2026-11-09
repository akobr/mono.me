using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Coverage;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.General;

// Command Line and Exit Codes: what every command shares: the usage and the version, parse errors, no prompt on redirected
// input, an unreachable API, no leaked secret, and SFORM_HOME as the only place of the CLI's files.
[Area(Area.General)]
public sealed partial class CommandLineTests : CliTest
{
    // GEN-CLI-06: what sform account prints first without SFORM_HOME when SFORM_TOKEN_STORE=file asks for a plain session.
    private const string IgnoredTokenStore = "SFORM_TOKEN_STORE=file is ignored without SFORM_HOME; the session is kept in the protected store.";

    // GEN-CLI-03: the hint of an edit that has no editor to open (EditorService.PromptEditorPreference).
    private const string NoEditorHint = "Choose one with sform account settings editor in a terminal, set it with sform account settings set editor.type, or set $VISUAL or $EDITOR.";

    // GEN-CLI-03: each command ends within this time on redirected input.
    private static readonly TimeSpan RedirectedTimeout = TimeSpan.FromSeconds(10);

    // A run that has no reason to wait; a longer one waited for something.
    private static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(60);

    // GEN-CLI-01: sform without a command, --version, and --help on every command print the usage or the version and exit 0.
    // In an empty home (nothing needs a sign-in): sform prints its usage ("Usage: sform [command] [options]", -v|--version
    // and its commands); sform --version and sform -v print the same version line, the file version and the informational
    // version in parentheses; then <path> --help for every command of the command tree (read from the build of the CLI as
    // the coverage test reads it) prints "Usage: sform <path>", every form of every option of the command and the names of
    // its subcommands. Every run ends with 0.
    [Test]
    [TestCase(391)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("sform", "-v", "--version")]
    public async Task Help_EveryCommand_Prints()
    {
        var tree = ReadCommandTree();

        var usage = (await Sform([])).ShouldSucceed().ShouldContain("Usage: sform [command] [options]");
        AssertListsOptionsAndCommands(usage, tree.Root);

        var version = (await Sform("--version")).ShouldSucceed().ShouldMatch(VersionLine());
        (await Sform("-v")).ShouldSucceed().ShouldMatch(VersionLine()).ShouldContain(version.PlainStdOut.Trim());

        using var parallel = new SemaphoreSlim(4);
        var runs = tree.Commands.Select(async command =>
        {
            await parallel.WaitAsync();
            try
            {
                var help = (await Sform(new SformRunOptions { Timeout = QuickTimeout }, [.. CommandTree.Words(command.Path), "--help"])).ShouldSucceed();
                help.ShouldContain(command.IsRoot ? "Usage: sform" : $"Usage: sform {command.Path}");
                AssertListsOptionsAndCommands(help, command);
            }
            finally
            {
                parallel.Release();
            }
        });
        await Task.WhenAll(runs);
    }

    // GEN-CLI-02: An unknown command or option fails with the parse code and a suggestion.
    // In an empty home: sform storytelr ends with -1 (input parsing), names the unknown command ("Unrecognized command or
    // argument 'storytelr'") and suggests storyteller ("The most similar commands/arguments are" with "> storyteller"
    // among them: McMaster says "is" only for a single suggestion, and storytelr is near scheduler too); sform story --nope
    // ends with -1 and "Unrecognized option '--nope'".
    [Test]
    [TestCase(392)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("sform")]
    public async Task Parse_Unknown_Fails()
    {
        (await Sform("storytelr"))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("Unrecognized command or argument 'storytelr'")
            .ShouldContain("The most similar commands/arguments are")
            .ShouldMatch(new Regex(@"(?m)^\s*>\s*storyteller\s*$"));

        (await Sform("story", "--nope"))
            .ShouldExitWith(CliExit.InputParsing)
            .ShouldContain("Unrecognized option '--nope'");
    }

    // GEN-CLI-03: No command waits for input when stdin is redirected.
    // As an Editor, with <rst> and its configuration (a=1), p.json a JSON Patch that changes a, a deleted annotation <del>, a
    // public pair <key> (v1) and kv.json changing it (v2): with the input redirected and without -y|--yes, the option or the
    // value, each of account set; account points get; points grant <id>; points revoke <id>; points create; assist; assist
    // key; story set <del>; story delete <rst> --permanent; config patch <rst> -i p.json; config patch <rst>; config edit
    // <rst>; template edit unt; schema type|annotation|descendant edit; kv delete <key> --permanent; kv purge <key>; kv
    // recipients remove <id>; kv import kv.json; kv set <new key> --secret; account machine renew <id> -c m.pfx ends within
    // 10 seconds: a value with 2, a confirmation or a terminal with 43, stderr saying "! sform cannot ask for …: the input is
    // redirected." and the option to use, or what to run instead (points create and assist key in a terminal, assist with
    // its subcommand). Without an editor (the home has none, and the run no $VISUAL or $EDITOR), config edit <rst> -y passes
    // the confirmation and stops with 43 at the question for the editor, naming sform account settings editor and $VISUAL or
    // $EDITOR. Nothing was written: <rst> and its configuration, <del>, <key>, the new key and the default project are as
    // before.
    [Test]
    [TestCase(393)]
    [UserRole(Role.Editor)]
    [Covers("sform")]
    public async Task RedirectedInput_NeverHangs()
    {
        var key = CliData.ResponsibilityKey();
        var deleted = CliData.ResponsibilityKey();
        var pair = CliData.KeyValueKey();
        var newPair = CliData.KeyValueKey();
        var accountId = await AccountIdAsync();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed();
        (await Sform(InProject("story", "set", deleted))).ShouldSucceed();
        (await Sform(InProject("story", "delete", deleted))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", pair, "v1", "--protection", "public"))).ShouldSucceed();
        await WriteFileAsync("p.json", """[ { "op": "replace", "path": "/a", "value": 2 } ]""");
        await WriteFileAsync("kv.json", $$"""{ "{{pair}}": "v2" }""");

        const string saveHint = "Pass -y|--yes to save them without asking.";
        RedirectedStep[] steps =
        [
            new(["account", "set"], CliExit.WrongInput, ["Pass it with -p|--projectKey <organizationName>.<projectName>."]),
            new(["account", "points", "get"], CliExit.WrongInput, ["Pass it with -p|--pointKey <organizationName>[.<projectName>]."]),
            new(["account", "points", "grant", accountId], CliExit.WrongInput, ["Pass it with -p|--point <organizationName>[.<projectName>]."]),
            new(["account", "points", "revoke", accountId], CliExit.WrongInput, ["Pass it with -p|--point <organizationName>[.<projectName>]."]),
            new(["account", "points", "create"], CliExit.InteractionNeeded, ["Run sform account points create in a terminal."]),
            new(["assist"], CliExit.InteractionNeeded, ["Name it: sform assist key."]),
            new(["assist", "key"], CliExit.InteractionNeeded, ["Run sform assist key in a terminal."]),
            new(InProject("story", "set", deleted), CliExit.WrongInput, ["--restore-deleted", "--recreate"]),
            new(InProject("story", "delete", key, "--permanent"), CliExit.InteractionNeeded, ["Pass -y|--yes to delete without asking."]),
            new(InProject("story", "config", "patch", key, "-i", "p.json"), CliExit.InteractionNeeded, ["Pass -y|--yes to apply it without asking."]),
            new(InProject("story", "config", "patch", key), CliExit.InteractionNeeded, ["Pass -y|--yes to apply it without asking."]),
            new(InProject("story", "config", "edit", key), CliExit.InteractionNeeded, [saveHint]),
            new(InProject("story", "template", "edit", CliData.Unit), CliExit.InteractionNeeded, [saveHint]),
            new(InProject("story", "schema", "type", "edit", CliData.Unit), CliExit.InteractionNeeded, [saveHint]),
            new(InProject("story", "schema", "annotation", "edit", key), CliExit.InteractionNeeded, [saveHint]),
            new(InProject("story", "schema", "descendant", "edit", key, CliData.Unit), CliExit.InteractionNeeded, [saveHint]),
            new(InProject("story", "kv", "delete", pair, "--permanent"), CliExit.InteractionNeeded, ["Pass -y|--yes to remove the key without asking."]),
            new(InProject("story", "kv", "purge", pair), CliExit.InteractionNeeded, ["Pass -y|--yes to purge without asking."]),
            new(InProject("story", "kv", "recipients", "remove", CliData.Slug("rcp")), CliExit.InteractionNeeded, ["Pass -y|--yes to remove the recipient without asking."]),
            new(InProject("story", "kv", "import", "kv.json"), CliExit.InteractionNeeded, ["Pass -y|--yes to import without asking, or --dry-run to see the plan."]),
            new(InProject("story", "kv", "set", newPair, "--secret"), CliExit.WrongInput, ["Pass it with --stdin, with -i|--import <file>, or as an argument."]),
            new(InProject("account", "machine", "renew", CliData.MachineName(), "-c", "m.pfx"), CliExit.WrongInput, ["--password-stdin"]),
            new(InProject("story", "config", "edit", key, "-y"), CliExit.InteractionNeeded, [NoEditorHint]),
        ];

        foreach (var step in steps)
        {
            var result = (await Sform(new SformRunOptions { Timeout = RedirectedTimeout, Label = "input redirected" }, step.Arguments))
                .ShouldExitWith(step.Exit)
                .ShouldMatch(CannotAsk(), SformStream.StdErr);
            foreach (var hint in step.Hints)
            {
                result.ShouldContain(hint, SformStream.StdErr);
            }
        }

        (await Sform(InProject("story", "get", key))).ShouldSucceed();
        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["a"]?.ToString()).IsEqualTo("1");
        (await Sform(InProject("story", "get", deleted))).ShouldFail();
        var stored = (await Sform(InProject("story", "kv", "get", pair))).ShouldSucceed().JsonNode();
        await Assert.That(stored["Value"]?.GetValue<string>()).IsEqualTo("v1");
        (await Sform(InProject("story", "kv", "get", newPair))).ShouldFail();
        await Assert.That(Home.HasDefaultProject).IsFalse();
    }

    // GEN-CLI-04: An unreachable API fails with a service failure, not a crash.
    // In an empty home, sform story -p … with SFORM_general__baseUrl pointing at a closed port on the loopback ends with 3
    // (not 1), "! The 2S platform could not be reached: <reason>" and "The API is <base URL> (general.baseUrl,
    // SFORM_general__baseUrl, or STORYTELLER_ENDPOINT for a machine); check the address and the network.", and no stack
    // trace.
    [Test]
    [TestCase(394)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("sform")]
    public async Task Api_Unreachable_ServiceFailure()
    {
        var baseUrl = $"http://127.0.0.1:{ClosedPort()}/api";

        (await Sform(new SformRunOptions { BaseUrl = baseUrl, Timeout = QuickTimeout, Label = "against a closed port" }, InProject("story")))
            .ShouldExitWith(CliExit.ServiceFailure)
            .ShouldMatch(new Regex(@"(?m)^! The 2S platform could not be reached: \S.*$"))
            .ShouldContain($"The API is {baseUrl} (general.baseUrl, SFORM_general__baseUrl, or STORYTELLER_ENDPOINT for a machine); check the address and the network.")
            .ShouldNotContain("Total mayhem")
            .ShouldNotMatch(StackFrame())
            .ShouldNotMatch(StackFrame(), SformStream.StdErr);
    }

    // GEN-CLI-05: No secret, API key or token appears in the output, the log file or the artifacts of a run.
    // As a ContributorWithSecrets: a secret pair <secret>, a configuration of <rst> bound to it, an API key machine of the
    // project (-r -k ApiKey) and GITHUB_ENV naming an empty file. As the user, sform story kv get <secret> --reveal (the
    // value, where asked) and sform story config export <rst> --github-env --prefix APP_ (the value only in its
    // ::add-mask:: line); as the machine, sform account and sform story get <rst>. The API key and the tokens of the user's
    // session appear in no standard output or error of these runs, the secret only where asked, and none of them in a log
    // file of the homes (SFORM_HOME/logs) or in an artifact of the test (the redacted runs).
    [Test]
    [TestCase(396)]
    [UserRole(Role.Secrets)]
    [Covers("sform")]
    public async Task Output_NeverLeaksSecrets()
    {
        var key = CliData.ResponsibilityKey();
        var pair = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", pair, secret, "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", $"token=@{pair}"))).ShouldSucceed();
        var machine = await CreateMachineAsync("-r", "-k", "ApiKey");
        var apiKey = machine.AccessKey ?? throw machine.Result.Fail("printed no API key");
        var githubEnv = await WriteFileAsync("github_env", string.Empty);
        var tokens = SessionTokens();

        var revealed = (await Sform(InProject("story", "kv", "get", pair, "--reveal"))).ShouldSucceed();
        await Assert.That(revealed.JsonNode()["Value"]?.GetValue<string>()).IsEqualTo(secret);
        var exported = (await Sform(
                new SformRunOptions { Environment = { [SformProcessEnvironment.GitHubEnv] = githubEnv } },
                InProject("story", "config", "export", key, "--github-env", "--prefix", "APP_")))
            .ShouldSucceed()
            .ShouldContain($"::add-mask::{secret}");
        var asMachine = AsMachine(machine.ApiKeyCredentials);
        var account = (await Sform(asMachine, "account")).ShouldSucceed();
        var read = (await Sform(asMachine, "story", "get", key)).ShouldSucceed();
        tokens.UnionWith(SessionTokens());

        string[] hidden = [apiKey, .. tokens];
        foreach (var run in new[] { revealed, exported, account, read })
        {
            foreach (var value in hidden)
            {
                run.ShouldNotContain(value).ShouldNotContain(value, SformStream.StdErr);
            }
        }

        revealed.ShouldNotContain(secret, SformStream.StdErr);
        exported.ShouldNotContain(secret, SformStream.StdErr);
        await Assert.That(exported.PlainStdOut.Replace($"::add-mask::{secret}", string.Empty, StringComparison.Ordinal)).DoesNotContain(secret);
        account.ShouldNotContain(secret).ShouldNotContain(secret, SformStream.StdErr);
        read.ShouldNotContain(secret).ShouldNotContain(secret, SformStream.StdErr);

        string[] everything = [.. hidden, secret];
        var logs = new[] { Home.Path, account.Home, read.Home }
            .Select(home => FileSystem.Path.Combine(home, SformHome.LogsDirectoryName))
            .Where(FileSystem.Directory.Exists)
            .SelectMany(directory => FileSystem.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));
        await AssertHoldNoneAsync(logs, everything, "the log file");

        var artifacts = Scope.Artifacts.Artifacts
            .Where(artifact => artifact.Kind != ArtifactKind.Screenshot && FileSystem.File.Exists(artifact.Path))
            .Select(artifact => artifact.Path);
        await AssertHoldNoneAsync(artifacts, everything, "the artifact");
    }

    // GEN-CLI-06: SFORM_HOME keeps every file of the CLI; SFORM_TOKEN_STORE=file without SFORM_HOME is ignored.
    // In an empty SFORM_HOME, sform account [-o <AuthKit organization id>] (the device code sign-in) and sform account set -p
    // <org>.<project>: the default project, the session and the log are in SFORM_HOME, and nothing is new or changed in the
    // folder of the CLI's build or in the user's .42for.net folder; neither run says the token store is ignored. Then sform
    // account with SFORM_TOKEN_STORE=file and without SFORM_HOME, stopped after its first line, prints "SFORM_TOKEN_STORE=file
    // is ignored without SFORM_HOME; the session is kept in the protected store." first, and another command (sform assist)
    // with the same variables does not print it.
    [Test]
    [TestCase(398)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("sform")]
    public async Task StateDirectory_IsolatesFiles()
    {
        var cliFolder = FileSystem.Path.GetDirectoryName(FileSystem.Path.GetFullPath(Runner.Locator.SformPath))!;
        var userFolder = UserFolder();
        var cliBefore = GeneralChecks.Snapshot(FileSystem, cliFolder);
        var userBefore = GeneralChecks.Snapshot(FileSystem, userFolder);

        var signIn = await SignInAsync();
        signIn.Result.ShouldNotContain("is ignored");
        (await Sform("account", "set", "-p", ProjectKey))
            .ShouldSucceed()
            .ShouldNotContain(SformProcessEnvironment.TokenStore);

        var files = Home.Files();
        await Assert.That(files).Contains(SformHome.SettingsFileName);
        await Assert.That(files).Contains(SformHome.AuthKitSessionFileName);
        await Assert.That(files.Any(file => file.StartsWith(SformHome.LogsDirectoryName, StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(GeneralChecks.Changes(cliBefore, GeneralChecks.Snapshot(FileSystem, cliFolder))).IsEmpty();
        await Assert.That(GeneralChecks.Changes(userBefore, GeneralChecks.Snapshot(FileSystem, userFolder))).IsEmpty();

        var withoutHome = new SformRunOptions
        {
            WithoutHome = true,
            Environment = { [SformProcessEnvironment.TokenStore] = "file", [SformProcessEnvironment.DeviceCodeOutput] = "json" },
            Label = "without SFORM_HOME",
        };
        await using (var account = await StartSform(withoutHome, "account"))
        {
            account.CloseInput();
            var notice = await account.WaitForLineOrExitAsync(SformStream.StdOut, line => line.Contains(IgnoredTokenStore, StringComparison.Ordinal), QuickTimeout);
            var stopped = await account.KillAsync();
            if (notice is null)
            {
                throw stopped.Fail($"did not say \"{IgnoredTokenStore}\"");
            }

            var first = stopped.Lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (first is null || !first.Contains(IgnoredTokenStore, StringComparison.Ordinal))
            {
                throw stopped.Fail($"printed \"{first}\" before \"{IgnoredTokenStore}\"");
            }
        }

        (await Sform(withoutHome with { Label = "another command without SFORM_HOME", Timeout = QuickTimeout }, "assist"))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldNotContain(SformProcessEnvironment.TokenStore)
            .ShouldNotContain(SformProcessEnvironment.TokenStore, SformStream.StdErr);
    }

    // The command tree of the build of the CLI, as the command coverage reads it.
    private static CommandTree ReadCommandTree()
    {
        return CoverageContext.Unavailable() is { } reason
            ? throw new InvalidOperationException($"The command tree cannot be read: {reason}")
            : CoverageContext.Current.Tree;
    }

    // The help lists every form of every option of the command and every subcommand's name.
    private static void AssertListsOptionsAndCommands(SformResult help, CliCommand command)
    {
        var text = help.PlainStdOut;
        var missing = command.Options.SelectMany(option => option.Forms)
            .Concat(command.Children.Select(child => child.Name))
            .Where(token => !Regex.IsMatch(text, $@"(?<![\w-]){Regex.Escape(token)}(?![\w-])", RegexOptions.CultureInvariant))
            .ToArray();
        if (missing.Length > 0)
        {
            throw help.Fail($"printed no {string.Join(", ", missing)} in the help of '{command.Path}'");
        }
    }

    // A port of the loopback that nothing listens on: one the system just gave out and that was closed again.
    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // The user's .42for.net folder, where sform keeps its session without SFORM_HOME (CliPaths: the local application data
    // on Windows, the home folder elsewhere).
    private string UserFolder()
    {
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return FileSystem.Path.Combine(root, ".42for.net");
    }

    // The tokens of the user's stored AuthKit session (access_token, refresh_token), registered as secrets of the test so a
    // leak is masked in the report too; empty when the home holds none.
    private HashSet<string> SessionTokens()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (Home.ReadText(SformHome.AuthKitSessionFileName) is not { Length: > 0 } text)
        {
            return tokens;
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject session)
            {
                foreach (var name in new[] { "access_token", "refresh_token" })
                {
                    if (session[name] is JsonValue value && value.TryGetValue<string>(out var token) && token.Length >= 8)
                    {
                        tokens.Add(token);
                        AddSecret(token);
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // not the plain session of SFORM_TOKEN_STORE=file: nothing to read
        }

        return tokens;
    }

    // None of the files holds any of the values.
    private async Task AssertHoldNoneAsync(IEnumerable<string> files, IReadOnlyList<string> values, string what)
    {
        foreach (var file in files)
        {
            string text;
            try
            {
                text = await FileSystem.File.ReadAllTextAsync(file);
            }
            catch (IOException)
            {
                continue;
            }

            if (values.Any(value => text.Contains(value, StringComparison.Ordinal)))
            {
                Assert.Fail($"A secret, the API key or a token of the session is in {what} {file}.");
            }
        }
    }

    [GeneratedRegex(@"^\d+(\.\d+){1,3} \(.+\)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"(?m)^\s+at \S+\(.*\)", RegexOptions.CultureInvariant)]
    private static partial Regex StackFrame();

    [GeneratedRegex(@"(?m)^! sform cannot ask for .+: the input is redirected\.\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CannotAsk();

    // A command of GEN-CLI-03, the code it ends with on redirected input and what its standard error names.
    private sealed record RedirectedStep(string[] Arguments, CliExit Exit, string[] Hints);
}
