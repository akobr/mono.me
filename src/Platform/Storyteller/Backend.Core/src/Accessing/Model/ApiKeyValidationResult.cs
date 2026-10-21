namespace _42.Platform.Storyteller.Accessing.Model;

public record ApiKeyValidationResult(
    string Organization,
    string Project,
    string MachineAccessId,
    IReadOnlyList<MachineScope> Scopes,
    MachineCredentialKind? CredentialKind = null);
