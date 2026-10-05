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

// Machine access as Entra ID app registrations with a client secret and an app role on the
// Storyteller API application. MachineAccess.Id is the appId (the token azp), ObjectId the
// application object ID.
// TODO: [P2] add support of certificate for better security
public class AzureAdMachineAccessService : IIdentityProviderMachineAccessService
{
    public const string ReadRoleKey = "DefaultRead";
    public const string ReadWriteRoleKey = "DefaultReadWrite";

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

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var clientId = _options.ClientId;
        var isReadWrite = model.Scope is MachineAccessScope.DefaultReadWrite
            or MachineAccessScope.AnnotationReadWrite
            or MachineAccessScope.ConfigurationReadWrite;

        // TODO: [P2] Make support for all roles and make it configurable in easier way
        var roleKey = isReadWrite ? ReadWriteRoleKey : ReadRoleKey;
        var roleId = _options.AppRoles.TryGetValue(roleKey, out var configuredRoleId)
            ? Guid.Parse(configuredRoleId)
            : throw new InvalidOperationException($"Missing Auth:AppRoles:{roleKey}.");

        var application = new Application
        {
            // TODO: [P3] organization and project should have max size (100 chars?)
            DisplayName = $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}",
            SignInAudience = "AzureADMyOrg",
            Tags = ["42", "sform", "machine"],
            Notes = $"organization={model.Organization}|project={model.Project}|scope={model.Scope:G}",
            RequiredResourceAccess =
            [
                new RequiredResourceAccess
                {
                    ResourceAppId = clientId,
                    ResourceAccess = [new ResourceAccess { Id = roleId, Type = "Role" }],
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

        // Grant admin consent programmatically: a service principal with the app role assigned.
        var createdSp = await _client.ServicePrincipals.PostAsync(new ServicePrincipal { AppId = appId });

        var resourceSpList = await _client.ServicePrincipals
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"appId eq '{clientId}'";
            });
        var resourceSp = resourceSpList?.Value?.FirstOrDefault();

        if (resourceSp?.Id is not null && createdSp?.Id is not null)
        {
            await _client.ServicePrincipals[createdSp.Id].AppRoleAssignments.PostAsync(new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(createdSp.Id),
                ResourceId = Guid.Parse(resourceSp.Id),
                AppRoleId = roleId,
            });
        }

        return new MachineAccess
        {
            Id = appId,
            ObjectId = objectId,
            AccessKey = secret,
            AnnotationKey = model.AnnotationKey,
            Scope = isReadWrite ? MachineAccessScope.DefaultReadWrite : MachineAccessScope.DefaultRead,
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
