namespace _42.Platform.Storyteller.Mcp.Understanding;

// Who can read a configuration. The platform records no runtime reads, so this is the machines whose scopes and
// annotation restriction (MachineAccess.AnnotationKey: that annotation and its descendants) reach the values. Machines
// is null when they could not be listed, and Reason says why.
public sealed record ConfigurationConsumers
{
    public const string RuntimeNote =
        "The platform does not record which applications or machines read a configuration at runtime. The machines below "
        + "can read these values: their scopes allow configuration reads and their annotation restriction reaches them, on "
        + "this annotation (ReadsThisDocument) or only through a descendant of it that exists, below their restriction "
        + "(ReadsThroughDescendants) or shared with it (ReadsThroughSharedDescendants), where the descendant may override them.";

    public string Note { get; init; } = RuntimeNote;

    public MachineGroups? Machines { get; init; }

    public string? Reason { get; init; }

    // ReadsThisDocument: unrestricted machines and machines restricted to the key or an ancestor of it.
    // ReadsThroughDescendants: machines restricted to a descendant; they read these values through their own effective
    // configuration, where the descendant may override them.
    // ReadsThroughSharedDescendants: machines restricted to an annotation that is neither an ancestor nor a descendant
    // but shares descendants with the key (rst.billing and sbt.northwind share usg.northwind.billing); they read these
    // values through such a descendant when it exists, where it may override them.
    public sealed record MachineGroups(
        IReadOnlyList<Machine> ReadsThisDocument,
        IReadOnlyList<Machine> ReadsThroughDescendants,
        IReadOnlyList<Machine> ReadsThroughSharedDescendants);

    // A machine without its credentials: SecretsRead notes that a resolved read fills in secret bindings for it.
    public sealed record Machine(
        string Id,
        string? AnnotationKey,
        IReadOnlyList<Sdk.MachineScope> Scopes,
        Sdk.MachineAccessCredentialKind CredentialKind,
        bool SecretsRead);
}
