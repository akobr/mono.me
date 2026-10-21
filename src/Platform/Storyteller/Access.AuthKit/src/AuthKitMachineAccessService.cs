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

        // The slugs come first, so a scope without its permission fails before WorkOS is called.
        var scopes = model.Scopes ?? [];
        var slugs = GetPermissionSlugs(scopes);
        var application = await _client.CreateM2MApplicationAsync(new WorkOsM2MApplicationCreate(
            $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}",
            BuildDescription(model, scopes),
            organizationId,
            slugs));

        try
        {
            var secret = await _client.CreateClientSecretAsync(application.Id);

            return new MachineAccess
            {
                Id = application.ClientId,
                ObjectId = application.Id,
                AccessKey = secret.Secret!,
                Scopes = scopes,
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

    // The inverse of PermissionMap, 1:1: for each scope the one WorkOS permission that maps its scope string.
    // The M2M token carries these slugs in its scope claim, and the API needs every scope both stored and carried,
    // so the application gets exactly the requested scopes or is not created at all.
    internal IReadOnlyList<string> GetPermissionSlugs(IReadOnlyList<MachineScope> scopes)
    {
        var slugs = new List<string>();

        foreach (var scope in scopes.Distinct())
        {
            var scopeString = MachineScopes.ToScopeString(scope) ?? scope.ToString();

            // The startup validation allows one slug per machine scope; the order keeps an unvalidated map deterministic.
            var slug = _options.PermissionMap
                .Where(entry => string.Equals(entry.Value.Trim(), scopeString, StringComparison.Ordinal))
                .Select(entry => entry.Key)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new MachineAccessNotSupportedException(
                    $"No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope {scopeString}.");

            slugs.Add(slug);
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

    // The same convention as the Entra ID and Keycloak machine services: the enum names, comma-joined.
    private static string BuildDescription(MachineAccessCreate model, IReadOnlyList<MachineScope> scopes)
    {
        var description = $"organization={model.Organization}|project={model.Project}|scopes={string.Join(',', scopes)}";

        return string.IsNullOrWhiteSpace(model.AnnotationKey)
            ? description
            : $"{description}|annotation={model.AnnotationKey}";
    }
}
