using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests.Access;

// Another pool user of the test (CliTest.AsAnotherUserAsync, AsGrantedUserAsync, ArrangeAsEditorAsync), with the
// user's own signed-in home of the session: e.g. an Editor who creates the data a Reader's case reads, the second user
// of a grant (AccountIdAsync), or the creator of a machine (CliTest.CreateMachineAsync with MachineCreation.As). Its runs
// are the test's (numbered, attached, redacted) and sign the user in through the device code the first time.
public sealed class SformActor
{
    private readonly Func<SformActor, SformRunOptions, string[], Task<SformResult>> run;
    private string? accountId;

    internal SformActor(TestUser user, SformHome home, Func<SformActor, SformRunOptions, string[], Task<SformResult>> run)
    {
        User = user;
        Home = home;
        this.run = run;
    }

    public TestUser User { get; }

    public SformHome Home { get; }

    public Task<SformResult> Sform(params string[] arguments)
    {
        return Sform(new SformRunOptions(), arguments);
    }

    public Task<SformResult> Sform(SformRunOptions options, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(options);
        return run(this, options, arguments);
    }

    // The user's account id ("Account ID: #<id>" of sform account), what account points grant takes; read once.
    public async Task<string> AccountIdAsync()
    {
        return accountId ??= AccountOutput.AccountIdOf(await Sform(new SformRunOptions { Label = $"the account id of {User}" }, "account"));
    }

    public override string ToString()
    {
        return User.ToString();
    }
}
