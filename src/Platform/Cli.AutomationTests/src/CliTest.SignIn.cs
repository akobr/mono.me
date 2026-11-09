using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests;

// The sign-in of the pool users: a user's home is signed in through the device code the first time a run needs it
// (DeviceCodeSignIn: sform account, the AuthKit device page in a browser context of the scope), and a run that ends with
// the CLI's general refusal (44 and GeneralRefusal: the API no longer accepts the session) signs in again once and runs
// once more. A [FreshSignIn] test signs in itself with SignInAsync. Another pool user (AsAnotherUserAsync) works in a
// home of its own, signed in the same way.
public abstract partial class CliTest
{
    private DeviceCodeSignIn? deviceCode;

    // The device code sign-in of this attempt.
    protected DeviceCodeSignIn DeviceCode => deviceCode ??= new DeviceCodeSignIn(Runner, Scope, Target);

    // Signs the leased user in through the device code, into the home (Home by default); with options a test of the
    // sign-in changes the command, -o, the variables, or declines. Throws when sform does not end signed in (unless it
    // declines, or ExpectSignedIn is false; then the result's exit code is the outcome).
    protected Task<DeviceCodeSignInResult> SignInAsync(SformHome? into = null, DeviceCodeOptions? options = null)
    {
        return SignInAsync(User, into ?? Home, options);
    }

    protected Task<DeviceCodeSignInResult> SignInAsync(TestUser user, SformHome into, DeviceCodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(into);
        return DeviceCode.SignInAsync(user, into, options, TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None);
    }

    // A second pool user for the test (returned at its end), with the user's own home of the session, signed in on its
    // first run; never a feature user unless the requirement is one (FeatureUser, WithoutFeatureUsers).
    protected async Task<SformActor> AsAnotherUserAsync(UserRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        var other = await LeaseAnotherUserAsync(WithoutFeatureUsers(requirement));
        var otherHome = SformHomes.ForUser(Session, other);
        otherHome.ResetDefaults();
        return new SformActor(other, otherHome, (actor, options, arguments) => Sform(options with { Home = actor.Home, Label = options.Label ?? $"as {actor.User}" }, arguments));
    }

    // An Editor (Contributor or higher of Target:Project) who creates the data that the test's role reads, in the acting
    // project (InProject): ArrangeAsAsync(EditingUser).
    protected Task<SformActor> ArrangeAsEditorAsync()
    {
        return ArrangeAsAsync(EditingUser);
    }

    // A user of the requirement (of Target:Project: EditingUser, SecretsUser, AdminUser) who creates the data that the
    // test's role reads or is refused, in the acting project (InProject): the leased user when it satisfies the
    // requirement, else another pool user (also for a [MachineOnly] test, which leases nobody), e.g. a
    // ContributorWithSecrets who sets the secret a Contributor's or a Reader's case is refused. A role project of
    // Target:RoleProjects has no such user (every pool user holds the role there), so a test that arranges data acts in
    // Target:Project.
    protected async Task<SformActor> ArrangeAsAsync(UserRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (RoleProject is { } project)
        {
            throw new InvalidOperationException(
                $"The test arranges data as a user with {requirement} in the project of its role, and Target:RoleProjects:{ActingRole} maps it to '{project}', where every pool user holds {ActingRole}; leave the role unmapped (a {ActingRole} of Target:Project acts) or provision the data there.");
        }

        if (LeasesUser && requirement.IsSatisfiedBy(User))
        {
            return new SformActor(User, Home, (actor, options, arguments) => Sform(options, arguments));
        }

        return await AsAnotherUserAsync(requirement);
    }

    // The user's home is signed in before a user run in it.
    private async Task EnsureSignedInFor(SformRunOptions options)
    {
        if (options.Machine is not null || options.Home is not { Kind: SformHomeKind.User, IsSignedIn: false } userHome)
        {
            return;
        }

        var signIn = await SignInAsync(OwnerOf(userHome), userHome);
        if (!signIn.IsSignedIn)
        {
            throw signIn.Result.Fail("did not sign the pool user in");
        }
    }

    private async Task<SformResult> RenewExpiredSessionAsync(IReadOnlyList<string> arguments, SformRunOptions options, SformResult result)
    {
        if (result.ExitCode != (int)CliExit.Unauthorized
            || !options.RenewExpiredSession
            || options.Machine is not null
            || options.Home is not { Kind: SformHomeKind.User } userHome
            || !result.PlainStdOut.Contains(GeneralRefusal, StringComparison.Ordinal))
        {
            return result;
        }

        // sform account reuses a session it can refresh ("already logged in"): then the refusal was real. A session it
        // cannot use any more starts a device code, which the sign-in approves, and the run goes once more.
        var signIn = await SignInAsync(OwnerOf(userHome), userHome);
        return signIn.Prompt is null ? result : await Runner.RunAsync(arguments, options with { RenewExpiredSession = false });
    }

    // The pool user a user's home belongs to: the leased user or one of the test's other users.
    private TestUser OwnerOf(SformHome userHome)
    {
        if (LeasesUser && (userHome.Owner is null || userHome.Owner == User.Fingerprint))
        {
            return User;
        }

        return Scope.ExtraUsers.FirstOrDefault(user => user.Fingerprint == userHome.Owner)
            ?? throw new InvalidOperationException($"The home {userHome.Path} belongs to a pool user ({userHome.Owner}) this test did not lease.");
    }
}
