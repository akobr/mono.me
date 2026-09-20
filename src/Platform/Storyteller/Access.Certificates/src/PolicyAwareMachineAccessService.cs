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

    public PolicyAwareMachineAccessService(
        ApiKeyMachineAccessService apiKeyService,
        CertificateMachineAccessService certificateService,
        IMachineAuthenticationPolicyStore policyStore,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<PolicyAwareMachineAccessService> logger)
    {
        _apiKeyService = apiKeyService;
        _certificateService = certificateService;
        _policyStore = policyStore;
        _options = options;
        _logger = logger;
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var credentialKind = await ResolveCredentialKindAsync(model.Organization, model.Project);

        return credentialKind switch
        {
            MachineCredentialKind.ApiKey => await CreateApiKeyOnlyAsync(model),
            MachineCredentialKind.Certificate => await CreateCertificateOnlyAsync(model),
            MachineCredentialKind.CertificateAndApiKey => await CreateCertificateAndApiKeyAsync(model),
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

    private async Task<MachineCredentialKind> ResolveCredentialKindAsync(string organization, string project)
    {
        var policy = await _policyStore.GetAsync(organization, project);
        return policy?.CredentialKind ?? _options.Value.DefaultCredentialKind;
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
