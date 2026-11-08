namespace _42.Platform.Cli.AutomationTests.Access;

// How CliTest.CreateMachineAsync creates a machine: as whom, in which project, and whether the cleanup deletes it.
//   await CreateMachineAsync("-r");                                                        // the leased user, acting project
//   var admin = await AsAnotherUserAsync(AdminUser);
//   await CreateMachineAsync(new MachineCreation { As = admin }, "-m", "SchemaWrite");       // a [MachineOnly] case's creator
//   await CreateMachineAsync(new MachineCreation { ProjectKey = disposable }, "-k", "ApiKey");
public sealed record MachineCreation
{
    // The pool user who creates the machine and whose cleanup deletes it (AsAnotherUserAsync, AsGrantedUserAsync,
    // ArrangeAsEditorAsync); null: the leased user. A [MachineOnly] test leases nobody, so it creates through another user.
    public SformActor? As { get; init; }

    // -p of the create and of the delete; null: the acting project (ProjectKey, or the project of UseProject).
    public string? ProjectKey { get; init; }

    // -v of the create and of the delete; null: the acting view when ProjectKey is null, else "default".
    public string? View { get; init; }

    // false: no cleanup (the test deletes it itself, or keeps it). Otherwise the cleanup deletes it, and a machine that is
    // gone by then (the test deleted it) counts as deleted.
    public bool DeleteAtCleanup { get; init; } = true;
}
