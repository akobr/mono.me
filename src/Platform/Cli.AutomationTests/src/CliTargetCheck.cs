using System.Text;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests;

// The check of the target before the first test, through sform itself (spec §3.4), as the check's pool user: the one the
// library leased the check (any role: the first free user of Users:Pool), or, when that one stands for a user feature
// (CliFeatures.OfUsers: an unregistered account, a user of two organizations), another pool user who stands for none
// (UserOfCheckAsync), as only that feature's tests ever lease a feature user (CliTest.WithoutFeatureUsers):
//   1. sform starts at all (sform --version);
//   2. the user signs in through the device code into the user's home of the session (so the user's first test signs
//      in no more) — the AuthKit device page, the pool password and -o Target:AuthKitOrganizationId are checked here;
//   3. sform story -p <Target:Organization>.<Target:Project> -v <Target:View> lists the project's view: the organization
//      and the project exist and the user may read them. A view exists by its data (GEN-CTX-03: a view without data
//      lists "No annotation exists yet." with exit 0), so a wrong Target:View is not caught; the same for each
//      Target:RoleProjects entry, where every pool user holds the role;
//   4. each configured machine reads: sform story -p <its project> -v <its view> with its STORYTELLER_* variables, a
//      call to the API, so a wrong or revoked credential, a wrong project or an unreachable endpoint fails here (sform
//      account of an API key or certificate machine makes no call: "The API checks the credentials on the first
//      call."). The machine needs Annotation.Read for it.
// A failure throws a TargetCheckException that says what to fix; every CLI test then fails at once with it. The runs
// are attached to the check's folder (<run>/TargetCheck/CheckTarget/sform-<n>.txt).
public sealed class CliTargetCheck
{
    private readonly TargetCheckContext context;
    private readonly CancellationToken cancellationToken;
    private readonly CliTarget target;
    private readonly SformRunner runner;
    private readonly string workingDirectory;
    private TestUser user;

    public CliTargetCheck(TargetCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        this.context = context;
        this.cancellationToken = cancellationToken;
        user = context.User;
        target = context.Settings.Bind<CliTarget>(PlatformTargetSettings.Section);
        var cli = context.Settings.Bind<CliSettings>(CliSettings.Section);
        var scope = context.Scope;
        workingDirectory = SformHomes.NewWorkingDirectory(context.Session, "TargetCheck");
        runner = new SformRunner(new SformRunnerOptions
        {
            CliPath = cli.Path,
            Timeout = TimeSpan.FromMilliseconds(cli.TimeoutMs),
            BaseUrl = target.BaseUrl,
            WorkingDirectory = workingDirectory,
            Redact = scope.Redact,
            AddSecret = scope.AddSecret,
            Attach = (name, text) => scope.AttachTextAsync(name, text, cancellationToken: cancellationToken),
            CancellationToken = cancellationToken,
            FileSystem = context.Session.FileSystem,
        });
    }

    public async Task RunAsync()
    {
        try
        {
            await CheckAsync();
        }
        finally
        {
            SformHomes.RemoveDirectory(workingDirectory, context.Session.FileSystem);
        }
    }

    // The requirement of the user the check signs in instead of the one the library leased it: a pool user who stands for
    // no user feature when the leased one stands for one (CliFeatures.OfUsers); null when the leased user will do.
    internal static UserRequirement? ReplacementOf(TestUser leased)
    {
        ArgumentNullException.ThrowIfNull(leased);
        return CliFeatures.OfUsers.Any(leased.HasRole) ? UserRequirement.Any.Except([.. CliFeatures.OfUsers]) : null;
    }

    private async Task CheckAsync()
    {
        var scope = context.Scope;
        user = await UserOfCheckAsync();
        var probe = SformHomes.NewForTest(context.Session, SformHomeKind.Fresh);
        try
        {
            var version = await runner.RunAsync(["--version"], new SformRunOptions { Home = probe, Label = "does sform start" });
            if (version.ExitCode != 0)
            {
                throw new TargetCheckException($"sform ({runner.Locator.SformPath}, from {runner.Locator.Source}) does not start: sform --version ended with {version.ExitCode}. {Excerpt(version)}");
            }
        }
        finally
        {
            SformHomes.Remove(probe, context.Session.FileSystem);
        }

        var home = SformHomes.ForUser(context.Session, user);
        if (!home.IsSignedIn)
        {
            DeviceCodeSignInResult signIn;
            try
            {
                signIn = await new DeviceCodeSignIn(runner, scope, target).SignInAsync(user, home, cancellationToken: cancellationToken);
            }
            catch (SformAssertionException exception)
            {
                throw new TargetCheckException(
                    $"{user} could not sign in through sform's device code against {target.BaseUrl}: {exception.Message} Check the pool user's password (Users:Pool:{user.Index}:Password), Target:AuthKitOrganizationId and that the AuthKit environment of the target allows the device code (docs/Platform/Cli.AutomationTests/automation-tests.md, \"Sign-in\").");
            }

            if (!signIn.IsSignedIn)
            {
                throw new TargetCheckException($"{user} did not sign in through sform account: exit {signIn.Result.ExitCode}. {Excerpt(signIn.Result)}");
            }

            if (signIn.Result.PlainStdOut.Contains("account register", StringComparison.Ordinal))
            {
                throw new TargetCheckException($"{user} has no account on the target (sform account says to call sform account register); register the pool user first.");
            }
        }

        var failures = new StringBuilder();
        await CheckProjectAsync(home, target.Organization, target.Project, target.View, "Target:Organization, Target:Project and Target:View", failures);
        foreach (var (role, path) in target.RoleProjects)
        {
            var parts = path.Split('/');
            await CheckProjectAsync(home, parts[0], parts[1], parts.Length > 2 ? parts[2] : "default", $"Target:RoleProjects:{role}", failures);
        }

        var machines = context.Settings.Bind<MachineCatalog>(MachineCatalog.Section);
        foreach (var (name, machine) in machines)
        {
            var credentials = MachineCredentials.FromSettings(name, machine, target.BaseUrl, context.Settings.ResolvePath);
            var machineHome = SformHomes.NewForTest(context.Session, SformHomeKind.Machine);
            var apiKey = credentials.Variables.TryGetValue(MachineCredentials.ApiKeyVariable, out var key) ? key : null;
            var projectKey = credentials.ProjectKey ?? MachineCredentials.ProjectKeyOf(apiKey) ?? target.ProjectKey;
            var view = machine.View ?? "default";
            try
            {
                // A read, as sform account of an API key or certificate machine calls nothing.
                var read = await runner.RunAsync(["story", "-p", projectKey, "-v", view], new SformRunOptions { Machine = credentials, Home = machineHome, Label = $"Machines:{name} reads {projectKey}/{view}" });
                if (read.ExitCode != 0)
                {
                    failures.Append($"Machines:{name} cannot read {projectKey}/{view}: sform story -p {projectKey} -v {view} as the machine ended with {SformResult.Describe(read.ExitCode)}. {Excerpt(read)} Check its credential in the user secrets (Machines:{name}:ApiKey, …), that it is not revoked and holds Annotation.Read, its Project and its Endpoint. ");
                }
            }
            finally
            {
                SformHomes.Remove(machineHome, context.Session.FileSystem);
            }
        }

        if (failures.Length > 0)
        {
            throw new TargetCheckException(failures.ToString().TrimEnd());
        }
    }

    // The leased user, or another pool user who stands for no user feature when the leased one stands for one
    // (ReplacementOf); the other user goes back to the pool with the check's scope.
    private async Task<TestUser> UserOfCheckAsync()
    {
        if (ReplacementOf(context.User) is not { } requirement)
        {
            return context.User;
        }

        try
        {
            return await context.Scope.LeaseAnotherUserAsync(requirement, cancellationToken);
        }
        catch (UserLeaseException exception)
        {
            throw new TargetCheckException(
                $"The target check signs in a pool user who stands for no feature ({string.Join(", ", CliFeatures.OfUsers)}), as only the tests of a feature lease its user: {exception.Message} Add the role users to Users:Pool (docs/Platform/Cli.AutomationTests/automation-tests.md, \"Feature users\").");
        }
    }

    // The first lines of a run's output, redacted, for a message.
    private string Excerpt(SformResult result)
    {
        var lines = result.Lines.Concat(result.ErrorLines).Where(line => line.Trim().Length > 0).Take(6);
        return context.Scope.Redact($"It wrote: {string.Join(" | ", lines)}");
    }

    private async Task CheckProjectAsync(SformHome home, string organization, string project, string view, string settings, StringBuilder failures)
    {
        var projectKey = $"{organization}.{project}";
        var story = await runner.RunAsync(["story", "-p", projectKey, "-v", view], new SformRunOptions { Home = home, Label = $"read {projectKey}/{view} ({settings})" });
        if (story.ExitCode != 0)
        {
            failures.Append($"{user} cannot read the project {projectKey} ({settings}): sform story -p {projectKey} -v {view} ended with {SformResult.Describe(story.ExitCode)}. {Excerpt(story)} Create the organization and the project, and make the pool users members with their roles (docs/Platform/Cli.AutomationTests/automation-tests.md, \"Target\"). ");
        }
    }
}
