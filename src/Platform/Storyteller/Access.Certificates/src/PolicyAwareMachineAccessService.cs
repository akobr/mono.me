using _42.Platform.Storyteller.Accessing;
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

    public PolicyAwareMachineAccessService(
        ApiKeyMachineAccessService apiKeyService,
        CertificateMachineAccessService certificateService,
        IMachineAuthenticationPolicyStore policyStore,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<PolicyAwareMachineAccessService> logger,
        IIdentityProviderMachineAccessService? identityProviderService = null)
    {
        _apiKeyService = apiKeyService;
        _certificateService = certificateService;
        _policyStore = policyStore;
        _options = options;
        _logger = logger;
        _identityProviderService = identityProviderService;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var credentialKind = await ResolveCredentialKindAsync(model.Organization, model.Project);

        return credentialKind switch
        {
            MachineCredentialKind.ApiKey => await CreateApiKeyOnlyAsync(model),
            MachineCredentialKind.Certificate => await CreateCertificateOnlyAsync(model),
            MachineCredentialKind.CertificateAndApiKey => await CreateCertificateAndApiKeyAsync(model),
            MachineCredentialKind.ClientCredentials => await CreateClientCredentialsAsync(model),
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
    public async Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return existingAccess.CredentialKind == MachineCredentialKind.ClientCredentials
            ? await RequireIdentityProviderService().ResetMachineAccessAsync(existingAccess, organization, project)
            : await ResetMachineAccessAsync(existingAccess.Id, organization, project);
    }

    public async Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return existingAccess.CredentialKind == MachineCredentialKind.ClientCredentials
            ? await RequireIdentityProviderService().DeleteMachineAccessAsync(existingAccess, organization, project)
            : await DeleteMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }

    private async Task<MachineCredentialKind> ResolveCredentialKindAsync(string organization, string project)
    {
        var policy = await _policyStore.GetAsync(organization, project);
        return policy?.CredentialKind ?? _options.Value.DefaultCredentialKind;
    }

    private IIdentityProviderMachineAccessService RequireIdentityProviderService()
    {
        return _identityProviderService
            ?? throw new MachineAccessNotSupportedException("ClientCredentials machine access is not configured for this deployment.");
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
