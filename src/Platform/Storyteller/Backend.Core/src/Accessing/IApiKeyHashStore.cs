namespace _42.Platform.Storyteller.Accessing;

public interface IApiKeyHashStore
{
    Task StoreAsync(
        string organization,
        string project,
        string machineAccessId,
        string hashedSecret,
        IReadOnlyList<MachineScope> scopes);

    // Replaces the hash of an existing machine, only while it still holds currentHashedSecret, and changes nothing else.
    // False when another hash replaced it meanwhile; NotFoundException when the machine or its hash is gone (a delete
    // removes the hash first). Never creates a machine.
    Task<bool> ReplaceAsync(
        string organization,
        string project,
        string machineAccessId,
        string currentHashedSecret,
        string newHashedSecret);

    Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId);

    Task<bool> DeleteAsync(string organization, string project, string machineAccessId);
}

// Scopes are the stored scopes of the machine. CredentialKind is the kind the machine was created with; null when
// the store does not know it.
public record ApiKeyHashEntry(
    string HashedSecret,
    IReadOnlyList<MachineScope> Scopes,
    MachineCredentialKind? CredentialKind = null);
