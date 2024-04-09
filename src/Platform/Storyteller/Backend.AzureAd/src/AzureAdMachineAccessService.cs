using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Applications.Item.AddPassword;
using Microsoft.Graph.Applications.Item.RemovePassword;
using Microsoft.Graph.Models;

namespace _42.Platform.Storyteller;

// TODO: [P2] add support of certificate for better security
public class AzureAdMachineAccessService : IMachineAccessService
{
    private const int DefaultPasswordExpiracyInYears = 1000;

    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var application = new Application
        {
            // TODO: [P3] organization and project should have max size (100 chars?)
            DisplayName = $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}",
            SignInAudience = "AzureADMyOrg",
            Tags = new List<string> { "42", "sform", "machine" },
            Notes = $"organization={model.Organization}|project={model.Project}|scope={model.Scope:G}",
        };

        if (model.AnnotationKey is not null)
        {
            application.Notes += $"|annotation={model.AnnotationKey}";
        }

        var clientId = Environment.GetEnvironmentVariable("Auth:ClientId");

        // TODO: [P2] Make support for all roles and make it configurable in easier way
        switch (model.Scope)
        {
            case MachineAccessScope.DefaultReadWrite:
            case MachineAccessScope.AnnotationReadWrite:
            case MachineAccessScope.ConfigurationReadWrite:
            {
                var roleId = Environment.GetEnvironmentVariable("Auth:AppRoles:DefaultReadWrite")
                             ?? throw new InvalidOperationException("Missing Auth:AppRoles:DefaultReadWrite environment variable.");

                application.RequiredResourceAccess = new List<RequiredResourceAccess>
                {
                    new()
                    {
                        ResourceAppId = clientId,
                        ResourceAccess = new List<ResourceAccess>
                        {
                            new() { Id = new Guid(roleId), Type = "Role" },
                        },
                    },
                };
                break;
            }

            // case MachineAccessScope.DefaultRead:
            // case MachineAccessScope.AnnotationRead:
            // case MachineAccessScope.ConfigurationRead:
            default:
            {
                var roleId = Environment.GetEnvironmentVariable("Auth:AppRoles:DefaultRead")
                             ?? throw new InvalidOperationException("Missing Auth:AppRoles:DefaultReadWrite environment variable.");

                application.RequiredResourceAccess = new List<RequiredResourceAccess>
                {
                    new()
                    {
                        ResourceAppId = clientId,
                        ResourceAccess = new List<ResourceAccess>
                        {
                            new() { Id = new Guid(roleId), Type = "Role" },
                        },
                    },
                };
                break;
            }
        }

        // Create a new instance of the ApplicationClient class using the client secret credential.
        // TODO: [P1] pass correct tenant id
        var credentials = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = "8ddd03c1-fe1f-48c7-a7dc-26d22dd42310" });
        var client = new GraphServiceClient(credentials);
        var createdApplication = await client.Applications.PostAsync(application);

        if (createdApplication?.AppId is null
            || createdApplication.Id is null)
        {
            throw new InvalidOperationException("The machine registration in Azure AD failed.");
        }

        var appId = createdApplication.AppId!;
        var objectId = createdApplication.Id!;

        var createdSecret = await client.Applications[objectId].AddPassword.PostAsync(
            new AddPasswordPostRequestBody
            {
                PasswordCredential = new PasswordCredential
                {
                    StartDateTime = DateTimeOffset.UtcNow,
                    EndDateTime = DateTimeOffset.UtcNow.AddYears(DefaultPasswordExpiracyInYears),
                    DisplayName = "Default secret, never expires. (42.sform.generated)",
                },
            });

        if (createdSecret?.SecretText is null)
        {
            throw new InvalidOperationException("The machine registration in Azure AD failed.");
        }

        return new MachineAccess
        {
            Id = appId,
            ObjectId = objectId,
            AccessKey = createdSecret.SecretText,
            AnnotationKey = model.AnnotationKey,
            Scope = model.Scope <= MachineAccessScope.ConfigurationRead
                ? MachineAccessScope.DefaultRead
                : MachineAccessScope.DefaultReadWrite,
        };
    }

    public async Task<string?> ResetMachineAccessAsync(string objectId)
    {
        // Create a new instance of the ApplicationClient class using the client secret credential.
        var client = new GraphServiceClient(new DefaultAzureCredential());

        var application = await client.Applications[objectId].GetAsync();

        if (application is null)
        {
            return null;
        }

        foreach (var secret in (application.PasswordCredentials ?? Enumerable.Empty<PasswordCredential>())
                     .Where(pc => (pc.DisplayName ?? string.Empty).Contains("42.sform.generated", StringComparison.OrdinalIgnoreCase)))
        {
            await client.Applications[objectId].RemovePassword
                .PostAsync(new RemovePasswordPostRequestBody { KeyId = secret.KeyId });
        }

        var createdSecret = await client.Applications[objectId].AddPassword.PostAsync(
            new AddPasswordPostRequestBody
            {
                PasswordCredential = new PasswordCredential
                {
                    StartDateTime = DateTimeOffset.UtcNow,
                    EndDateTime = DateTimeOffset.UtcNow.AddYears(DefaultPasswordExpiracyInYears),
                    DisplayName = "Default secret, never expires. (42.sform.generated)",
                },
            });

        return createdSecret?.SecretText;
    }

    public async Task<bool> DeleteMachineAccessAsync(string objectId)
    {
        // Create a new instance of the ApplicationClient class using the client secret credential.
        var client = new GraphServiceClient(new DefaultAzureCredential());
        await client.Applications[objectId].DeleteAsync();
        return true;
    }
}
