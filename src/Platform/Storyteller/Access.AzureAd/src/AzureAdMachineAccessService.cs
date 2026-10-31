using System.Net;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Applications.Item.AddPassword;
using Microsoft.Graph.Applications.Item.RemovePassword;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace _42.Platform.Storyteller;

// Machine access as Entra ID app registrations with a client secret and app roles on the
// Storyteller API application: one per machine scope, so a machine gets exactly its scopes.
// MachineAccess.Id is the appId (the token azp), ObjectId the application object ID.
// TODO: [P2] add support of certificate for better security
public class AzureAdMachineAccessService : IIdentityProviderMachineAccessService
{
    private const int DefaultPasswordExpirationYears = 1000;
    private const string GeneratedSecretMarker = "42.sform.generated";

    private readonly GraphServiceClient _client;
    private readonly UserAuthenticationOptions _options;
    private readonly AzureAdMachineAccessOptions _machineOptions;

    public AzureAdMachineAccessService(
        GraphServiceClient client,
        IOptions<UserAuthenticationOptions> options,
        IOptions<AzureAdMachineAccessOptions> machineOptions)
    {
        _client = client;
        _options = options.Value;
        _machineOptions = machineOptions.Value;
    }

    // The role ID of each scope, keyed by its enum name (Auth:AppRoles:AnnotationRead, ...). The value of that app role
    // in Entra ID must be the scope string (Annotation.Read, ...; an App. prefix is allowed), so the token's roles claim
    // carries that scope. Every key is optional; a machine needs the roles of its scopes.
    public static string GetRoleKey(MachineScope scope)
    {
        return scope.ToString("G");
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var clientId = _options.ClientId;

        // Checked before the first Graph call, so a refused machine leaves no application behind.
        // In enum order: one role, and one assignment, per scope.
        var scopes = MachineScopes.Normalize(model.Scopes);
        var roleIds = GetRoleIds(scopes);

        // The app roles are assigned on the service principal of the Storyteller API. It does not depend on the new
        // application, so it is looked up before the first write too: without it no role can be assigned, and the
        // machine would be stored with scopes it was never given.
        var resourceSpList = await _client.ServicePrincipals
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"appId eq '{clientId}'";
            });
        var resourceSpId = resourceSpList?.Value?.FirstOrDefault()?.Id
            ?? throw new FeatureNotConfiguredException(
                $"The service principal of the Storyteller API application {clientId} was not found in the machine tenant, so no app role can be assigned.",
                ErrorCodes.MachineAccessNotConfigured);

        var application = new Application
        {
            // TODO: [P3] organization and project should have max size (100 chars?)
            DisplayName = $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}",
            SignInAudience = "AzureADMyOrg",
            Tags = ["42", "sform", "machine"],
            Notes = $"organization={model.Organization}|project={model.Project}|scopes={string.Join(',', scopes)}",
            RequiredResourceAccess =
            [
                new RequiredResourceAccess
                {
                    ResourceAppId = clientId,
                    ResourceAccess = roleIds.Select(id => new ResourceAccess { Id = id, Type = "Role" }).ToList(),
                },
            ],
        };

        if (model.AnnotationKey is not null)
        {
            application.Notes += $"|annotation={model.AnnotationKey}";
        }

        var createdApplication = await _client.Applications.PostAsync(application);

        if (createdApplication?.AppId is null
            || createdApplication.Id is null)
        {
            throw new InvalidOperationException("The machine registration in Azure AD failed.");
        }

        var appId = createdApplication.AppId;
        var objectId = createdApplication.Id;
        var secret = await AddSecretAsync(objectId)
            ?? throw new InvalidOperationException("The machine registration in Azure AD failed.");

        // Grant admin consent programmatically: a service principal with the app roles assigned.
        var createdSp = await _client.ServicePrincipals.PostAsync(new ServicePrincipal { AppId = appId });

        // Without its id no role can be assigned, so no scope is returned that the machine was not given.
        if (createdSp?.Id is null)
        {
            throw new InvalidOperationException("The service principal of the machine registration in Azure AD was not created, so its app roles cannot be assigned.");
        }

        foreach (var assignedRoleId in roleIds)
        {
            await _client.ServicePrincipals[createdSp.Id].AppRoleAssignments.PostAsync(new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(createdSp.Id),
                ResourceId = Guid.Parse(resourceSpId),
                AppRoleId = assignedRoleId,
            });
        }

        return new MachineAccess
        {
            Id = appId,
            ObjectId = objectId,
            AccessKey = secret,
            AnnotationKey = model.AnnotationKey,

            // The assigned roles, nothing collapsed: each scope has a role of its own.
            Scopes = scopes,
            CredentialKind = MachineCredentialKind.ClientCredentials,
            TokenEndpoint = _machineOptions.GetTokenEndpoint(),
            TokenScope = $"api://{clientId}/.default",
        };
    }

    public async Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        var application = await _client.Applications[objectId].GetAsync();

        if (application is null)
        {
            return null;
        }

        foreach (var secret in (application.PasswordCredentials ?? [])
                     .Where(pc => (pc.DisplayName ?? string.Empty).Contains(GeneratedSecretMarker, StringComparison.OrdinalIgnoreCase)))
        {
            await _client.Applications[objectId].RemovePassword
                .PostAsync(new RemovePasswordPostRequestBody { KeyId = secret.KeyId });
        }

        return await AddSecretAsync(objectId);
    }

    public Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return ResetMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }

    // False when the application no longer exists.
    public async Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        try
        {
            await _client.Applications[objectId].DeleteAsync();
            return true;
        }
        catch (ODataError error) when (error.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return DeleteMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }

    private IReadOnlyList<Guid> GetRoleIds(IReadOnlyList<MachineScope> scopes)
    {
        var roleIds = new List<Guid>();

        foreach (var scope in scopes)
        {
            var roleKey = GetRoleKey(scope);

            if (!_options.AppRoles.TryGetValue(roleKey, out var configuredRoleId)
                || !Guid.TryParse(configuredRoleId, out var roleId))
            {
                throw new FeatureNotConfiguredException(
                    $"Machines with the scope {MachineScopes.ToScopeString(scope)} need the app role Auth:AppRoles:{roleKey}, which this deployment does not configure.",
                    ErrorCodes.MachineAccessNotConfigured);
            }

            roleIds.Add(roleId);
        }

        return roleIds;
    }

    private async Task<string?> AddSecretAsync(string objectId)
    {
        var createdSecret = await _client.Applications[objectId].AddPassword.PostAsync(
            new AddPasswordPostRequestBody
            {
                PasswordCredential = new PasswordCredential
                {
                    StartDateTime = DateTimeOffset.UtcNow,
                    EndDateTime = DateTimeOffset.UtcNow.AddYears(DefaultPasswordExpirationYears),
                    DisplayName = $"Default secret, never expires. ({GeneratedSecretMarker})",
                },
            });

        return createdSecret?.SecretText;
    }
}
