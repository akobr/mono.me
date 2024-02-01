using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Access.Entities;
using _42.Platform.Storyteller.Access.Models;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Applications.Item.AddPassword;
using Microsoft.Graph.Applications.Item.RemovePassword;
using Microsoft.Graph.Models;

namespace _42.Platform.Storyteller.AzureAd;

// TODO: [P2] add support of certificate for better security
public class AzureAdMachineAccessService : IMachineAccessService
{
    public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var application = new Application
        {
            // TODO: [P3] organization and project should have max size (100 chars?)
            DisplayName = $"42.sform.{model.Organization}.{model.Project}.{Guid.NewGuid():N}",
            SignInAudience = "AzureADMyOrg",
            Tags = new List<string> { "42", "sform", "machine" },
            AdditionalData =
            {
                ["sform:organization"] = model.Organization,
                ["sform:project"] = model.Project,
                ["sform:scope"] = $"{model.Scope:G}",
            },
            PasswordCredentials = new List<PasswordCredential>
            {
                new()
                {
                    StartDateTime = DateTimeOffset.UtcNow,
                    DisplayName = "Default secret, never expires",
                    AdditionalData =
                    {
                        ["sform:isGenerated"] = true,
                    },
                },
            },
        };

        if (model.AnnotationKey is not null)
        {
            application.AdditionalData["sform:annotation"] = model.AnnotationKey;
        }

        // TODO: [P2] Make support for all roles
        switch (model.Scope)
        {
            case MachineAccessScope.DefaultReadWrite:
            case MachineAccessScope.AnnotationReadWrite:
            case MachineAccessScope.ConfigurationReadWrite:
                application.RequiredResourceAccess = new List<RequiredResourceAccess>
                {
                    new()
                    {
                        ResourceAppId = "7f37b203-3599-4c73-9796-39d96883198c",
                        ResourceAccess = new List<ResourceAccess>
                        {
                            new() { Id = new Guid("628499e7-e732-4c9e-927b-107395d67e2e"), Type = "Role" },
                        },
                    },
                };
                break;

            // case MachineAccessScope.DefaultRead:
            // case MachineAccessScope.AnnotationRead:
            // case MachineAccessScope.ConfigurationRead:
            default:
                application.RequiredResourceAccess = new List<RequiredResourceAccess>
                {
                    new()
                    {
                        ResourceAppId = "7f37b203-3599-4c73-9796-39d96883198c",
                        ResourceAccess = new List<ResourceAccess>
                        {
                            new() { Id = new Guid("659db1ff-e899-4dde-880e-028912428fc2"), Type = "Role" },
                        },
                    },
                };
                break;
        }

        // Create a new instance of the ApplicationClient class using the client secret credential.
        var client = new GraphServiceClient(new DefaultAzureCredential());
        var createdApplication = await client.Applications.PostAsync(application);

        if (createdApplication?.AppId is null
            || createdApplication.PasswordCredentials is null
            || createdApplication.PasswordCredentials.Count < 1)
        {
            throw new InvalidOperationException("The machine registration in Azure AD failed.");
        }

        return new MachineAccess
        {
            Id = createdApplication.AppId,
            AccessKey = createdApplication.PasswordCredentials.First().SecretText,
            PartitionKey = $"{model.Project}.access",
            AnnotationKey = model.AnnotationKey,
            Scope = model.Scope <= MachineAccessScope.ConfigurationRead
                ? MachineAccessScope.DefaultRead
                : MachineAccessScope.DefaultReadWrite,
        };
    }

    public async Task<string?> ResetMachineAccessAsync(string appId)
    {
        // Create a new instance of the ApplicationClient class using the client secret credential.
        var client = new GraphServiceClient(new DefaultAzureCredential());

        var application = await client.Applications[appId].GetAsync();

        if (application is null)
        {
            return null;
        }

        foreach (var secret in (application.PasswordCredentials ?? Enumerable.Empty<PasswordCredential>())
                     .Where(pc => pc.AdditionalData.ContainsKey("sform:isGenerated")))
        {
            await client.Applications[appId].RemovePassword
                .PostAsync(new RemovePasswordPostRequestBody { KeyId = secret.KeyId });
        }

        var createdSecret = await client.Applications[appId].AddPassword.PostAsync(
            new AddPasswordPostRequestBody
            {
                PasswordCredential = new PasswordCredential
                {
                    StartDateTime = DateTimeOffset.UtcNow,
                    DisplayName = "Default secret, never expires",
                    AdditionalData = { ["sform:isGenerated"] = true, },
                },
            });

        return createdSecret?.SecretText;
    }

    public async Task<bool> DeleteMachineAccessAsync(string appId)
    {
        // Create a new instance of the ApplicationClient class using the client secret credential.
        var client = new GraphServiceClient(new DefaultAzureCredential());
        await client.Applications[appId].DeleteAsync();
        return true;
    }
}
