using System.Diagnostics;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The runner against the built sform, without a target: it starts, it ends with the codes of ExitCodes, a prompt on the
// redirected input fails fast, the API of the run is the one given, the outer STORYTELLER_* variables do not reach it, and
// a timeout kills the process tree. Plain TUnit tests (not CliTest), so they run, also in the solution's run, while every
// CliTest is skipped without Target:BaseUrl.
[Category(CliCategories.SelfTest)]
public sealed class SformRunnerSelfTests
{
    [Test]
    public async Task Sform_IsBuiltAndRecorded()
    {
        var locator = SformLocator.Resolve(null);

        await Assert.That(SformLocator.RecordedPath).IsNotNull();
        await Assert.That(locator.SformPath).EndsWith("42.Platform.Cli.dll");
        await Assert.That(new System.IO.Abstractions.FileSystem().File.Exists(locator.SformPath)).IsTrue();
    }

    [Test]
    public async Task Version_PrintsTheVersion()
    {
        using var sandbox = new SelfTestSandbox();

        var result = (await sandbox.RunAsync("--version")).ShouldSucceed();

        await Assert.That(result.PlainStdOut).Matches(@"\d+\.\d+");
    }

    [Test]
    public async Task Help_ListsTheCommands()
    {
        using var sandbox = new SelfTestSandbox();

        var result = (await sandbox.RunAsync("--help")).ShouldSucceed();

        result.ShouldContain("account").ShouldContain("storyteller").ShouldNotContain("Total mayhem");
        await Assert.That(result.IsTerminal).IsFalse();
    }

    // GEN-CLI-02 without a target: the parse code, -1.
    [Test]
    public async Task UnknownCommand_EndsWithTheParseCode()
    {
        using var sandbox = new SelfTestSandbox();

        var result = await sandbox.RunAsync("storytelr");

        result.ShouldExitWith(CliExit.InputParsing);
        await Assert.That(result.Exit).IsEqualTo(CliExit.InputParsing);
    }

    // ACC-TLS-03 without a target: assist key asks before any call, so on the closed standard input it stops at once.
    [Test]
    public async Task PromptOnRedirectedInput_FailsFast()
    {
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(60));

        var result = await sandbox.RunAsync("assist", "key");

        result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("the input is redirected", SformStream.StdErr)
            .ShouldContain("Run sform assist key in a terminal.", SformStream.StdErr);
        await Assert.That(result.Duration).IsLessThan(TimeSpan.FromSeconds(30));
    }

    // GEN-CLI-04 without a target: SFORM_general__baseUrl reaches sform, and a closed port is a service failure.
    [Test]
    public async Task UnreachableApi_IsAServiceFailure()
    {
        using var sandbox = new SelfTestSandbox();

        var result = await sandbox.RunAsync("story", "-p", "org1.proj1");

        result.ShouldExitWith(CliExit.ServiceFailure)
            .ShouldContain("The 2S platform could not be reached")
            .ShouldContain(sandbox.ClosedApi);
    }

    // Input reaches the standard input, which is then closed: a shell in sform's place reads the first line and then the
    // end of the input (it would wait for ever if the input stayed open).
    [Test]
    public async Task Input_IsWrittenThenClosed()
    {
        var locator = OperatingSystem.IsWindows()
            ? SformLocator.Custom("powershell.exe", "-NoProfile", "-NonInteractive", "-Command")
            : SformLocator.Custom("/bin/sh", "-c");
        var script = OperatingSystem.IsWindows()
            ? "$first = [Console]::In.ReadLine(); $rest = [Console]::In.ReadToEnd(); Write-Output \"first=$first rest=$($rest.Trim())\""
            : "read first; rest=$(cat); echo \"first=$first rest=$rest\"";
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(60), locator: locator);

        var result = await sandbox.Runner.RunAsync([script], new SformRunOptions { Input = "v3\nmore\n" });

        result.ShouldSucceed().ShouldContain("first=v3 rest=more");
    }

    // A prompt that asks for a value an option gives ends with 2 on the closed input: account set without -p.
    [Test]
    public async Task ValuePromptOnRedirectedInput_IsWrongInput()
    {
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(60));

        var result = await sandbox.RunAsync("account", "set");

        result.ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("! sform cannot ask for the default project: the input is redirected.", SformStream.StdErr)
            .ShouldContain("-p|--projectKey", SformStream.StdErr);
    }

    // The outer STORYTELLER_* variables never reach sform: with a machine's API key in the tests' own environment,
    // account logout still sees no machine; with the same key given to the run, it does.
    [Test]
    [NotInParallel("process-environment")]
    public async Task OuterStorytellerVariables_DoNotLeak()
    {
        using var sandbox = new SelfTestSandbox();
        const string fakeKey = "2s.b3JnMTpwcm9qMTptMQ.not-a-real-secret";

        var before = Environment.GetEnvironmentVariable(MachineCredentials.ApiKeyVariable);
        Environment.SetEnvironmentVariable(MachineCredentials.ApiKeyVariable, fakeKey);
        try
        {
            var asUser = await sandbox.RunAsync("account", "logout");
            asUser.ShouldNotContain("machine credentials");

            var asMachine = await sandbox.Runner.RunAsync(
                ["account", "logout"],
                new SformRunOptions { Machine = MachineCredentials.ForApiKey(fakeKey, sandbox.ClosedApi) });
            asMachine.ShouldSucceed().ShouldContain("sform uses the machine credentials in STORYTELLER_API_KEY");
        }
        finally
        {
            Environment.SetEnvironmentVariable(MachineCredentials.ApiKeyVariable, before);
        }
    }

    // A run's variables win over the runner's, and null removes one: SFORM_HOME stays (it is not optional).
    [Test]
    public async Task Environment_IsBuiltFromScratch()
    {
        var machine = new System.Collections.Hashtable
        {
            ["PATH"] = "/usr/bin",
            ["STORYTELLER_API_KEY"] = "leaked",
            ["SFORM_HOME"] = "/elsewhere",
            ["SFORM_general__baseUrl"] = "http://leaked",
            ["BROWSER"] = "firefox",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["GITHUB_ENV"] = "/tmp/github_env",
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = "leaked",
            ["LC_ALL"] = "C.UTF-8",
        };

        var environment = SformProcessEnvironment.Build(
            "/homes/one",
            [new Dictionary<string, string?> { ["SFORM_TOKEN_STORE"] = "file", ["NO_COLOR"] = null, ["SFORM_HOME"] = "/ignored" }],
            machine);

        await Assert.That(environment["SFORM_HOME"]).IsEqualTo("/homes/one");
        await Assert.That(environment["PATH"]).IsEqualTo("/usr/bin");
        await Assert.That(environment["LC_ALL"]).IsEqualTo("C.UTF-8");
        await Assert.That(environment["DOTNET_ENVIRONMENT"]).IsEqualTo("Production");
        await Assert.That(environment["DO_NOT_TRACK"]).IsEqualTo("1");
        await Assert.That(environment["SFORM_TOKEN_STORE"]).IsEqualTo("file");
        await Assert.That(environment.ContainsKey("NO_COLOR")).IsFalse();
        foreach (var name in new[] { "STORYTELLER_API_KEY", "SFORM_general__baseUrl", "BROWSER", "GITHUB_ENV", "ACTIONS_ID_TOKEN_REQUEST_TOKEN" })
        {
            await Assert.That(environment.ContainsKey(name)).IsFalse();
        }
    }

    // A timeout kills the whole tree: a shell that started a long child is gone with the child, and the run fails with
    // what it wrote so far.
    [Test]
    public async Task Timeout_KillsTheProcessTree()
    {
        var locator = OperatingSystem.IsWindows()
            ? SformLocator.Custom("powershell.exe", "-NoProfile", "-NonInteractive", "-Command")
            : SformLocator.Custom("/bin/sh", "-c");
        var script = OperatingSystem.IsWindows()
            ? "$p = Start-Process -FilePath ping.exe -ArgumentList '-n','120','127.0.0.1' -PassThru -NoNewWindow; Write-Output \"child $($p.Id)\"; Wait-Process -Id $p.Id"
            : "sleep 120 & echo \"child $!\"; wait";
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(8), locator: locator);

        var started = Stopwatch.StartNew();
        SformTimeoutException? timeout = null;
        try
        {
            await sandbox.RunAsync(script);
        }
        catch (SformTimeoutException exception)
        {
            timeout = exception;
        }

        await Assert.That(timeout).IsNotNull();
        await Assert.That(started.Elapsed).IsLessThan(TimeSpan.FromSeconds(60));
        var match = System.Text.RegularExpressions.Regex.Match(timeout!.Message, @"child (\d+)");
        await Assert.That(match.Success).IsTrue();

        var childId = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(await HasEndedAsync(childId)).IsTrue();
    }

    // A process that sform's stand-in started and left running holds the redirected streams: the run still ends soon after
    // the stand-in, and what it left is killed (a job object on Windows, the process tag elsewhere).
    [Test]
    public async Task LeftoverChild_DoesNotHoldTheRun()
    {
        var locator = OperatingSystem.IsWindows()
            ? SformLocator.Custom("powershell.exe", "-NoProfile", "-NonInteractive", "-Command")
            : SformLocator.Custom("/bin/sh", "-c");
        var script = OperatingSystem.IsWindows()
            ? "$p = Start-Process -FilePath ping.exe -ArgumentList '-n','60','127.0.0.1' -PassThru -NoNewWindow; Write-Output \"child $($p.Id)\""
            : "sleep 60 & echo \"child $!\"";
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(50), locator: locator);

        var started = Stopwatch.StartNew();
        var result = await sandbox.RunAsync(script);

        result.ShouldSucceed();
        await Assert.That(started.Elapsed).IsLessThan(TimeSpan.FromSeconds(30));
        var match = System.Text.RegularExpressions.Regex.Match(result.PlainStdOut, @"child (\d+)");
        await Assert.That(match.Success).IsTrue();
        var childId = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(await HasEndedAsync(childId)).IsTrue();
    }

    // On Windows a .NET child reads and writes its redirected streams in the OEM code page of its hidden console: what
    // the test sends arrives as sent, and the "│" of sform's gutter comes back as "│" (Windows PowerShell, a .NET
    // program, in sform's place). Elsewhere everything is UTF-8.
    [Test]
    public async Task ConsoleEncoding_RoundTripsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The console code page matters only on Windows; Linux and macOS read and write UTF-8.");
        }

        var oem = Running.Processes.ConsoleEncodings.ChildConsole;
        var sample = new string("│äé╗".Where(character => oem.GetString(oem.GetBytes(character.ToString())) == character.ToString()).ToArray());
        var locator = SformLocator.Custom("powershell.exe", "-NoProfile", "-NonInteractive", "-Command");
        using var sandbox = new SelfTestSandbox(timeout: TimeSpan.FromSeconds(60), locator: locator);

        var result = await sandbox.Runner.RunAsync(
            ["[Console]::Out.Write('<' + [char]0x2502 + [Console]::In.ReadToEnd().Trim() + '>')"],
            new SformRunOptions { Input = sample + "\n" });

        result.ShouldSucceed().ShouldContain($"<│{sample}>");
    }

    private static async Task<bool> HasEndedAsync(int processId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
