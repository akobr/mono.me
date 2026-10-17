using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class PolicyAwareMachineAccessService : IMachineAccessService
{
    private readonly ApiKeyMachineAccessService _apiKeyService;
    private readonly CertificateMachineAccessService _certificateService;
    private readonly IMachineAuthenticationPolicyStore _policyStore;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly ILogger<PolicyAwareMachineAccessService> _logger;
    private readonly IIdentityProviderMachineAccessService? _identityProviderService;
    private readonly IFederatedMachineAccessService? _federatedService;

    public PolicyAwareMachineAccessService(
        ApiKeyMachineAccessService apiKeyService,
        CertificateMachineAccessService certificateService,
        IMachineAuthenticationPolicyStore policyStore,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<PolicyAwareMachineAccessService> logger,
        IIdentityProviderMachineAccessService? identityProviderService = null,
        IFederatedMachineAccessService? federatedService = null)
    {
        _apiKeyService = apiKeyService;
        _certificateService = certificateService;
        _policyStore = policyStore;
        _options = options;
        _logger = logger;
        _identityProviderService = identityProviderService;
        _federatedService = federatedService;
    }

    // The machine picks one of the kinds its project enables (the project default when it names none).
    // A certificate gets the requested lifetime, else the project default. A CertificateAndApiKey machine
    // that uses a shared certificate gets no certificate of its own, so no lifetime either.
    // Only a Federated machine has federation rules.
    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var policy = await _policyStore.GetEffectiveAsync(model.Organization, model.Project, _options.Value);
        var credentialKind = model.CredentialKind ?? policy.CredentialKind!.Value;

        if (!policy.IsEnabled(credentialKind))
        {
            throw new MachineAccessNotSupportedException(
                $"The credential kind {credentialKind} is not enabled in the project {model.Organization}.{model.Project}.");
        }

        var usesSharedCertificate = model.UseSharedCertificate == true;

        if (usesSharedCertificate && credentialKind != MachineCredentialKind.CertificateAndApiKey)
        {
            throw new MachineAccessNotSupportedException(
                $"Only {MachineCredentialKind.CertificateAndApiKey} machines can use a shared certificate, not {credentialKind}.");
        }

        if (credentialKind != MachineCredentialKind.Federated && model.FederationRules is { Count: > 0 })
        {
            throw new InvalidInputException(FederationValidation.RulesOnOtherKindMessage, ErrorCodes.MachineNotFederated);
        }

        model = model with
        {
            CredentialKind = credentialKind,
            CertificateLifetimeDays = credentialKind.IssuesCertificate() && !usesSharedCertificate
                ? model.CertificateLifetimeDays ?? policy.CertificateLifetimeDays
                : null,
            UseSharedCertificate = usesSharedCertificate,
            FederationRules = credentialKind == MachineCredentialKind.Federated ? model.FederationRules : null,
        };

        return credentialKind switch
        {
            MachineCredentialKind.ApiKey => await CreateApiKeyOnlyAsync(model),
            MachineCredentialKind.Certificate => await CreateCertificateOnlyAsync(model),
            MachineCredentialKind.CertificateAndApiKey => await CreateCertificateAndApiKeyAsync(model),
            MachineCredentialKind.ClientCredentials => await CreateClientCredentialsAsync(model),
            MachineCredentialKind.Federated => await CreateFederatedAsync(model),
            _ => throw new InvalidOperationException($"Unknown credential kind: {credentialKind}"),
        };
    }

    public async Task<MachineAccess> ExtendMachineAccessAsync(MachineAccess existingAccess, MachineAccessCreate model)
    {
        return await _certificateService.ExtendWithCertificateAsync(existingAccess, model);
    }

    public async Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        // Reset delegates to the API key service for the key portion.
        return await _apiKeyService.ResetMachineAccessAsync(objectId, organization, project);
    }

    public async Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        // Delete from both — best effort for each.
        var apiKeyDeleted = await _apiKeyService.DeleteMachineAccessAsync(objectId, organization, project);

        try
        {
            await _certificateService.RevokeCertificateAsync(organization, project, objectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke certificate for {ObjectId} — may not have had one", objectId);
        }

        return apiKeyDeleted;
    }

    // Routed by the kind the access was created with: the project policy may have changed since.
    // A Federated machine never reaches the API key or certificate services.
    public async Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return existingAccess.CredentialKind switch
        {
            MachineCredentialKind.ClientCredentials => await RequireIdentityProviderService().ResetMachineAccessAsync(existingAccess, organization, project),
            MachineCredentialKind.Federated => await RequireFederatedService().ResetMachineAccessAsync(existingAccess, organization, project),
            _ => await ResetMachineAccessAsync(existingAccess.Id, organization, project),
        };
    }

    // A Federated machine has nothing outside its document, so it can be deleted even after federation was switched off.
    public async Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return existingAccess.CredentialKind switch
        {
            MachineCredentialKind.ClientCredentials => await RequireIdentityProviderService().DeleteMachineAccessAsync(existingAccess, organization, project),
            MachineCredentialKind.Federated => _federatedService is null
                || await _federatedService.DeleteMachineAccessAsync(existingAccess, organization, project),
            _ => await DeleteMachineAccessAsync(existingAccess.ObjectId, organization, project),
        };
    }

    private IIdentityProviderMachineAccessService RequireIdentityProviderService()
    {
        return _identityProviderService
            ?? throw new MachineAccessNotSupportedException("ClientCredentials machine access is not configured for this deployment.");
    }

    private IFederatedMachineAccessService RequireFederatedService()
    {
        return _federatedService
            ?? throw new MachineAccessNotSupportedException("Federated machine access is not configured for this deployment.");
    }

    private async Task<MachineAccess> CreateFederatedAsync(MachineAccessCreate model)
    {
        var result = await RequireFederatedService().CreateMachineAccessAsync(model);
        return result with { CredentialKind = MachineCredentialKind.Federated };
    }

    private async Task<MachineAccess> CreateClientCredentialsAsync(MachineAccessCreate model)
    {
        var result = await RequireIdentityProviderService().CreateMachineAccessAsync(model);
        return result with { CredentialKind = MachineCredentialKind.ClientCredentials };
    }

    private async Task<MachineAccess> CreateApiKeyOnlyAsync(MachineAccessCreate model)
    {
        var result = await _apiKeyService.CreateMachineAccessAsync(model);
        return result with { CredentialKind = MachineCredentialKind.ApiKey };
    }

    private async Task<MachineAccess> CreateCertificateOnlyAsync(MachineAccessCreate model)
    {
        return await _certificateService.CreateCertificateAsync(model);
    }

    private async Task<MachineAccess> CreateCertificateAndApiKeyAsync(MachineAccessCreate model)
    {
        // Phase 1: Create API key machine access.
        var apiKeyAccess = await _apiKeyService.CreateMachineAccessAsync(model);

        if (model.UseSharedCertificate == true)
        {
            // The certificate half is a shared certificate of the project, so none is issued for the machine.
            return apiKeyAccess with
            {
                CredentialKind = MachineCredentialKind.CertificateAndApiKey,
                UsesSharedCertificate = true,
            };
        }

        try
        {
            // Phase 2: Extend with certificate.
            var combined = await _certificateService.ExtendWithCertificateAsync(apiKeyAccess, model);
            return combined;
        }
        catch
        {
            // Compensation: roll back the API key.
            try
            {
                await _apiKeyService.DeleteMachineAccessAsync(apiKeyAccess.Id, model.Organization, model.Project);
            }
            catch (Exception compensationEx)
            {
                _logger.LogError(compensationEx, "Failed to compensate API key creation for {Id}", apiKeyAccess.Id);
            }

            throw;
        }
    }
}
