namespace _42.Platform.Storyteller.Accessing;

public interface IApiKeyHashStore
{
    Task StoreAsync(
        string organization,
        string project,
        string machineAccessId,
        string hashedSecret,
        MachineAccessScope scope,
        IReadOnlyList<MachineManagementScope>? managementScopes = null);

    Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId);

    Task<bool> DeleteAsync(string organization, string project, string machineAccessId);
}

// CredentialKind is the kind the machine was created with; null when the store does not know it.
// ManagementScopes are granted on top of Scope; null for none.
public record ApiKeyHashEntry(
    string HashedSecret,
    MachineAccessScope Scope,
    MachineCredentialKind? CredentialKind = null,
    IReadOnlyList<MachineManagementScope>? ManagementScopes = null);
