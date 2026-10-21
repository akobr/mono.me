using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller;

// Federated machines have no secret and nothing outside Storyteller: the machine is its rules,
// stored with the machine document by the access service.
public class FederatedMachineAccessService : IFederatedMachineAccessService
{
    public const string ResetNotSupportedMessage = "A Federated machine has no secret to reset. Change its federation rules instead.";

    private readonly IFederatedIssuerStore _issuerStore;

    public FederatedMachineAccessService(IFederatedIssuerStore issuerStore)
    {
        _issuerStore = issuerStore;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var issuers = await _issuerStore.ListAsync(model.Organization, model.Project);

        if (FederationValidation.ValidateRules(model.FederationRules, issuers) is { } error)
        {
            throw new InvalidInputException(error, ErrorCodes.InvalidFederation);
        }

        var id = Guid.NewGuid().ToString("D");

        // Exactly the requested scopes: the handler completed them, and the access service stores what comes back.
        return new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = string.Empty,
            Scopes = model.Scopes ?? [],
            AnnotationKey = model.AnnotationKey,
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = model.FederationRules!.ToList(),
        };
    }

    public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        throw new MachineAccessNotSupportedException(ResetNotSupportedMessage);
    }

    public Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        throw new MachineAccessNotSupportedException(ResetNotSupportedMessage);
    }

    // Nothing external exists; the access service deletes the machine document.
    public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        return Task.FromResult(true);
    }

    public Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return Task.FromResult(true);
    }
}
