using System.IO.Abstractions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests;

// The base class of the CLI's tests: an AutomationTest (no browser up front) that runs the real sform out of process
// against Target:BaseUrl, as the leased pool user or as a machine.
//   - Sform(args) runs sform to its end in the user's signed-in home and the test's working directory and returns an
//     SformResult (exit code, output, JSON, assertions); StartSform returns a running SformProcess (the device code, a
//     prompt); StartSformInTerminal one in a pseudo terminal (mode T).
//   - The leased user's home (SFORM_HOME) is signed in through the device code the first time it is needed (the user's
//     first test in the session, or the target check), and its default project and editor are removed before each test;
//     [FreshSignIn] gives the test an empty home instead, [MachineOnly] leases no user.
//   - InProject(args) adds -p <org>.<project> -v <view> of the project the acting role works in ([UserRole],
//     Target:RoleProjects), as the catalogue's rule says: a test starts with no default project.
//   - AsMachine("<name>") runs as a pre-provisioned machine (Machines:<name>), CreateMachineAsync creates one that the
//     cleanup deletes (as the leased user or another, MachineCreation).
//   - CreateDisposableProjectAsync creates a project of the test's own and acts in it (UseProject), AsGrantedUserAsync
//     gives another pool user a role there (the "second user" of a P case).
//   - Every run is attached as sform-<n>.txt (redacted); AddSecret registers a secret the test came by. The working
//     directory and the homes are outside the artifacts (SformHomes).
// TUnit creates an instance per attempt, so everything here belongs to one attempt. The roles and the skips are in
// CliTest.Roles.cs, the sign-in in CliTest.SignIn.cs, the machines in CliTest.Machines.cs, a disposable project in
// CliTest.Projects.cs.
public abstract partial class CliTest : AutomationTest
{
    // What the CLI prints for a 401, or a 403 without a message of the server (ApiRefusal.GeneralMessage of the CLI).
    public const string GeneralRefusal = "You are not authorized to execute the command or you don't have access to requested resources.";

    private readonly List<Func<Task>> cleanups = [];
    private readonly List<SformHome> testHomes = [];
    private readonly List<SformProcess> processes = [];
    private SformRunner? runner;
    private SformHome? home;
    private CliData? cliData;
    private string? workingDirectory;

    protected CliTarget Target => Settings.Bind<CliTarget>(PlatformTargetSettings.Section);

    protected CliSettings Cli => Settings.Bind<CliSettings>(CliSettings.Section);

    protected MachineCatalog Machines => Settings.Bind<MachineCatalog>(MachineCatalog.Section);

    // The generated names of this attempt (seeded): annotation keys (<rst>), key-value keys, values, labels.
    protected CliData CliData => cliData ??= new CliData(Data, Session.RunId);

    // The runner of this attempt (its runs are numbered sform-1.txt, sform-2.txt, …).
    protected SformRunner Runner => runner ??= CreateRunner();

    // The file system of the session (TestableIO): files the test writes or reads around sform's runs.
    protected IFileSystem FileSystem => Session.FileSystem;

    // The current directory of the test's runs, where the files sform reads or writes (-i, -o, -e, export zips, a
    // GITHUB_ENV file) live: a folder of the test under <temp>/sform-automation/<run id>/work/, outside the artifacts
    // (it may hold a private key or a resolved secret). Removed when the test passes; kept when it does not (or with
    // Cli:KeepWorkingDirectories), and then the list of its files is attached (work-files.txt). Attach a file a failure
    // needs with AttachFileAsync, which redacts it.
    protected string WorkingDirectory => workingDirectory ??= SformHomes.NewWorkingDirectory(
        Session,
        $"{GetType().Name}.{TestContext.Current?.Metadata.TestDetails.MethodName ?? "test"}");

    // The home of the test's user runs: the leased user's for the session (default project and editor removed before the
    // test), an empty one of the test with [FreshSignIn], an empty machine home with [MachineOnly].
    protected SformHome Home => home ??= CreateHome();

    // Runs sform as the leased user (signed in first when the home is not yet) in Home and WorkingDirectory.
    protected Task<SformResult> Sform(params string[] arguments)
    {
        return Sform(new SformRunOptions(), arguments);
    }

    // Runs sform with the options: a machine (AsMachine), another home (FreshHome()), input, variables, a timeout.
    protected async Task<SformResult> Sform(SformRunOptions options, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var complete = Complete(options);
        await EnsureSignedInFor(complete);
        var result = await Runner.RunAsync(arguments, complete);
        return await RenewExpiredSessionAsync(arguments, complete, result);
    }

    // Starts sform and returns while it runs (see SformProcess); disposed (killed when still running) at the end of the
    // test at the latest.
    protected Task<SformProcess> StartSform(params string[] arguments)
    {
        return StartSform(new SformRunOptions(), arguments);
    }

    protected async Task<SformProcess> StartSform(SformRunOptions options, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var complete = Complete(options);
        await EnsureSignedInFor(complete);
        var process = await Runner.StartAsync(arguments, complete);
        lock (processes)
        {
            processes.Add(process);
        }

        return process;
    }

    // Starts sform in a pseudo terminal (mode T; the test is [Terminal]): expect a prompt's text, send keys and lines.
    protected Task<SformProcess> StartSformInTerminal(params string[] arguments)
    {
        return StartSform(new SformRunOptions { Terminal = true }, arguments);
    }

    protected Task<SformProcess> StartSformInTerminal(SformRunOptions options, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        return StartSform(options with { Terminal = true }, arguments);
    }

    // The arguments and the context options of the acting role's project: -p <org>.<project> -v <view>.
    protected string[] InProject(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return [.. arguments, "-p", ProjectKey, "-v", View];
    }

    // A new empty home of this test (removed at its end), e.g. for a second, separate sign-in.
    protected SformHome FreshHome()
    {
        var created = SformHomes.NewForTest(Session, SformHomeKind.Fresh);
        lock (testHomes)
        {
            testHomes.Add(created);
        }

        return created;
    }

    // A secret the test came by (a value it set, a key a command printed): masked from now on in the attachments, the
    // report and the messages of the assertions. sform's own printed credentials are found by the runner already.
    protected void AddSecret(string value)
    {
        Scope.AddSecret(value);
    }

    // Writes a file into the working directory (UTF-8 without a BOM) and returns its full path.
    protected async Task<string> WriteFileAsync(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var path = FileSystem.Path.GetFullPath(FileSystem.Path.Combine(WorkingDirectory, relativePath));
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(path)!);
        await FileSystem.File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    // The text of a file of the working directory (or an absolute path).
    protected Task<string> ReadFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FileSystem.File.ReadAllTextAsync(FileSystem.Path.Combine(WorkingDirectory, path));
    }

    // Attaches a text file the test or sform wrote, redacted (Scope.Redact), as <name>.
    protected async Task AttachFileAsync(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = FileSystem.Path.Combine(WorkingDirectory, path);
        var text = await FileSystem.File.ReadAllTextAsync(full);
        await Scope.AttachTextAsync(name ?? FileSystem.Path.GetFileName(full), text, ArtifactKind.Other);
    }

    // Mode E: the fake editor (src/Platform/Cli.AutomationTests/editor) becomes the editor of the home (Home by default).
    // Pass what it does to each run with editor.With(EditorChange.Replace(…) | Merge(…) | None | Invalid | Exit(n) |
    // Sequence(…)) (one With call per sform run), and read what sform gave it in that run with editor.ReadLog(options);
    // its log and the changes' files are in the working directory (fake-editor/<id>/). Call it once per home: a second
    // call for the same home makes a new FakeEditor with a log of its own.
    //   var editor = await UseEditorAsync();
    //   var options = editor.With(EditorChange.Merge("""{ "a": 2 }"""));
    //   (await Sform(options, InProject("story", "config", "edit", key, "-y"))).ShouldSucceed();
    //   await Assert.That(editor.ReadLog(options).Single().OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(1);
    protected async Task<FakeEditor> UseEditorAsync(SformHome? home = null)
    {
        var editor = new FakeEditor(WorkingDirectory, FileSystem);
        await UseEditorAsync(editor.Command, home);
        return editor;
    }

    // Mode E with another program: the editor of the home (Home by default) becomes the custom command, in
    // editor.config.json ({ "editor": { "EditorType": "Custom", "CustomCommand": "<program> [arguments]" } }); ResetDefaults
    // removes it before the next test of the home. sform appends the quoted path of the file to edit and splits the command
    // at its first space (EditorService.ParseCustomCommand), so the program's path must contain no space.
    protected async Task UseEditorAsync(string customCommand, SformHome? home = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customCommand);

        var target = home ?? Home;
        target.Ensure();
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["editor"] = new System.Text.Json.Nodes.JsonObject
            {
                ["EditorType"] = "Custom",
                ["CustomCommand"] = customCommand,
            },
        };
        await FileSystem.File.WriteAllTextAsync(target.EditorConfigFile, json.ToJsonString(), new System.Text.UTF8Encoding(false));
    }

    // Runs at the end of the attempt, in reverse order of registration, before the homes of the test are removed.
    protected void OnCleanUp(Func<Task> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        lock (cleanups)
        {
            cleanups.Add(cleanup);
        }
    }

    // Kills what still runs, runs the registered cleanups (the first failure fails a passing attempt), removes the test's
    // own homes. A derived class that overrides it calls the base.
    protected override async Task CleanUpAsync()
    {
        Exception? failure = null;
        SformProcess[] running;
        lock (processes)
        {
            running = [.. processes];
        }

        foreach (var process in running)
        {
            await process.DisposeAsync();
        }

        Func<Task>[] registered;
        lock (cleanups)
        {
            registered = [.. cleanups];
        }

        foreach (var cleanup in registered.Reverse())
        {
            try
            {
                await cleanup();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }

        SformHome[] homes;
        lock (testHomes)
        {
            homes = [.. testHomes];
        }

        foreach (var testHome in homes)
        {
            SformHomes.Remove(testHome, FileSystem);
        }

        await EndWorkingDirectoryAsync(failure is null);
        await base.CleanUpAsync();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        }
    }

    // Removes the working directory of a test that passed; keeps the one of a test that did not (or every one with
    // Cli:KeepWorkingDirectories) and attaches the list of its files, not their contents.
    private async Task EndWorkingDirectoryAsync(bool cleanupsPassed)
    {
        if (workingDirectory is not { } directory || !FileSystem.Directory.Exists(directory))
        {
            return;
        }

        var passed = cleanupsPassed && TestContext.Current?.Execution.Result?.State is TestState.Passed;
        if (passed && !Cli.KeepWorkingDirectories)
        {
            SformHomes.RemoveDirectory(directory, FileSystem);
            return;
        }

        var files = FileSystem.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => $"{FileSystem.Path.GetRelativePath(directory, file)} ({FileSystem.FileInfo.New(file).Length} bytes)")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var listing = $"The working directory of the test (kept, outside the artifacts): {directory}\n\n{(files.Length == 0 ? "(no files)" : string.Join('\n', files))}\n";
        await Scope.AttachTextAsync("work-files.txt", listing, ArtifactKind.Other);
    }

    private SformRunner CreateRunner()
    {
        var cli = Cli;
        return new SformRunner(new SformRunnerOptions
        {
            CliPath = cli.Path,
            Timeout = TimeSpan.FromMilliseconds(cli.TimeoutMs),
            BaseUrl = Target.BaseUrl,
            WorkingDirectory = WorkingDirectory,
            Redact = Scope.Redact,
            AddSecret = Scope.AddSecret,
            Attach = (name, text) => Scope.AttachTextAsync(name, text),
            CancellationToken = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None,
            FileSystem = FileSystem,
        });
    }

    private SformHome CreateHome()
    {
        if (!LeasesUser)
        {
            return NewMachineHome();
        }

        if (HasCategory(CliCategories.FreshSignIn))
        {
            return FreshHome();
        }

        var userHome = SformHomes.ForUser(Session, User);
        userHome.ResetDefaults();
        return userHome;
    }

    private SformHome NewMachineHome()
    {
        var created = SformHomes.NewForTest(Session, SformHomeKind.Machine);
        lock (testHomes)
        {
            testHomes.Add(created);
        }

        return created;
    }

    // The options with the test's defaults: a machine's runs in an empty machine home of their own, a run without
    // SFORM_HOME with an empty home as its HOME, the user's in Home, all in WorkingDirectory.
    private SformRunOptions Complete(SformRunOptions options)
    {
        return options with
        {
            Home = options.Home ?? (options.Machine is not null ? NewMachineHome() : options.WithoutHome ? FreshHome() : Home),
            WorkingDirectory = options.WorkingDirectory ?? WorkingDirectory,
        };
    }
}
