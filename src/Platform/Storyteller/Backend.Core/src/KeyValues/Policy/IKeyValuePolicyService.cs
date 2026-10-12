namespace _42.Platform.Storyteller.KeyValues.Policy;

// The management of a project's policy: allowed levels, the HSM and BYOK keys, rotation, recipients.
public interface IKeyValuePolicyService
{
    Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default);

    Task<KeyValuePolicy> UpdateAsync(string organization, string project, KeyValuePolicyUpdate update, KeyValueCaller caller, CancellationToken cancellationToken = default);

    ByokSetupInfo GetByokSetup(string organization, string project);

    Task<KeyValuePolicy> VerifyByokAsync(string organization, string project, KeyValueCaller caller, CancellationToken cancellationToken = default);

    // Rotates the Standard or HSM key now and starts the re-wrap.
    Task<KeyValuePolicy> RotateAsync(string organization, string project, KeyValueProtection protection, KeyValueCaller caller, CancellationToken cancellationToken = default);

    Task<ClientEncryptionRecipient> AddRecipientAsync(string organization, string project, ClientEncryptionRecipientCreate create, KeyValueCaller caller, CancellationToken cancellationToken = default);

    Task<ClientEncryptionRecipient> UpdateRecipientAsync(string organization, string project, string id, ClientEncryptionRecipientUpdate update, KeyValueCaller caller, CancellationToken cancellationToken = default);

    Task RemoveRecipientAsync(string organization, string project, string id, KeyValueCaller caller, CancellationToken cancellationToken = default);
}
