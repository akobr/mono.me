using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Machine access as WorkOS Connect M2M applications in one configured organization.
// MachineAccess.Id is the application client ID (the M2M token sub), ObjectId the application ID.
public sealed class AuthKitMachineAccessService : IIdentityProviderMachineAccessService
{
    // WorkOS allows at most five client secrets per application.
    internal const int MaxClientSecrets = 5;

    private readonly WorkOsManagementClient _client;
    private readonly AuthKitOptions _options;
    private readonly ILogger<AuthKitMachineAccessService> _logger;

    public AuthKitMachineAccessService(
        WorkOsManagementClient client,
        IOptions<UserAuthenticationOptions> options,
        ILogger<AuthKitMachineAccessService> logger)
    {
        _client = client;
        _options = options.Value.AuthKit;
        _logger = logger;
    }

    private string TokenEndpoint => $"{_options.AuthKitDomain.TrimEnd('/')}/oauth2/token";

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var organizationId = _options.MachineOrganizationId;

        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new MachineAccessNotSupportedException("Auth:AuthKit:MachineOrganizationId is required for AuthKit machine access.");
        }

        var application = await _client.CreateM2MApplicationAsync(new WorkOsM2MApplicationCreate(
            $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}",
            BuildDescription(model),
            organizationId,
            GetPermissionSlugs(model.Scope)));

        try
        {
            var secret = await _client.CreateClientSecretAsync(application.Id);

            return new MachineAccess
            {
                Id = application.ClientId,
                ObjectId = application.Id,
                AccessKey = secret.Secret!,
                Scope = model.Scope,
                AnnotationKey = model.AnnotationKey,
                CredentialKind = MachineCredentialKind.ClientCredentials,
                TokenEndpoint = TokenEndpoint,
            };
        }
        catch
        {
            // Compensation: an application without a secret the caller ever saw is useless.
            try
            {
                await _client.DeleteApplicationAsync(application.Id);
            }
            catch (Exception compensationException)
            {
                _logger.LogError(compensationException, "Failed to delete the WorkOS application {ApplicationId} after its secret could not be created", application.Id);
            }

            throw;
        }
    }

    public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        return ResetSecretAsync(objectId);
    }

    public Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return ResetSecretAsync(existingAccess.ObjectId);
    }

    public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        return _client.DeleteApplicationAsync(objectId);
    }

    public Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return _client.DeleteApplicationAsync(existingAccess.ObjectId);
    }

    // The inverse of PermissionMap: every WorkOS permission whose Storyteller scopes all fall within
    // what the machine access scope grants. The M2M token then carries these slugs in its scope claim.
    internal IReadOnlyList<string> GetPermissionSlugs(MachineAccessScope scope)
    {
        var granted = MachineAccessScopes.Get(scope).ToHashSet(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var slugs = new List<string>();

        foreach (var (slug, mapped) in _options.PermissionMap.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var scopes = AuthKitClaimNormalizer.SplitScopes(mapped).ToList();

            if (scopes.Count > 0 && scopes.All(granted.Contains))
            {
                slugs.Add(slug);
                covered.UnionWith(scopes);
            }
        }

        if (slugs.Count == 0)
        {
            throw new MachineAccessNotSupportedException(
                $"No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scopes of {scope} ({string.Join(", ", granted)}).");
        }

        if (!covered.SetEquals(granted))
        {
            _logger.LogWarning(
                "Machine access scope {Scope} is only partly covered by Auth:AuthKit:PermissionMap; missing {MissingScopes}",
                scope,
                string.Join(", ", granted.Except(covered)));
        }

        return slugs;
    }

    // Mint first so the machine always has a working secret, then revoke the older ones.
    // When WorkOS is already at its limit, the oldest secret has to go first to make room.
    private async Task<string?> ResetSecretAsync(string applicationId)
    {
        var previous = (await _client.ListClientSecretsAsync(applicationId))
            .OrderBy(secret => secret.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (previous.Count >= MaxClientSecrets)
        {
            await _client.DeleteClientSecretAsync(previous[0].Id);
            previous.RemoveAt(0);
        }

        var created = await _client.CreateClientSecretAsync(applicationId);

        foreach (var secret in previous)
        {
            await _client.DeleteClientSecretAsync(secret.Id);
        }

        return created.Secret;
    }

    // The same convention as the Entra ID and Keycloak machine services.
    private static string BuildDescription(MachineAccessCreate model)
    {
        var description = $"organization={model.Organization}|project={model.Project}|scope={model.Scope}";
        return string.IsNullOrWhiteSpace(model.AnnotationKey)
            ? description
            : $"{description}|annotation={model.AnnotationKey}";
    }
}
