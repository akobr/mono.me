using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Access.Entities;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace _42.Platform.Storyteller.AzureAd;

public class AzureAdMachineAccessService : IMachineAccessService
{
    public Task<bool> CreateOrganizationAsync(string organization)
    {
        throw new NotImplementedException();
    }

    public async Task<MachineAccess> CreateMachineAccessAsync(string organization)
    {
        // Create a new client secret credential using the default Azure.Identity credential.
        var credential = new ClientSecretCredential(
            tenantId: "<your-tenant-id>",
            clientId: "<your-client-id>",
            clientSecret: "<your-client-secret>"
        );

        // Create a new instance of the ApplicationClient class using the client secret credential.
        var client = new GraphServiceClient(new DefaultAzureCredential());

        // Create a new password credential for the registered application.
        var passwordCredential = new PasswordCredential
        {
            DisplayName = "My new client secret",
            EndDateTime = DateTimeOffset.UtcNow.AddYears(1),
        };

        // Add the new password credential to the registered application.
        //await client.ServicePrincipals.UpdatePasswordCredentialsAsync(
        //    "<your-application-id>",
        //    new List<PasswordCredential> { passwordCredential }
        //);

        return new MachineAccess
        {
            Id = Guid.NewGuid().ToString("D"),
            AccessKey = Guid.NewGuid().ToString("N"),
            PartitionKey = string.Empty,
            Scope = MachineAccessScope.DefaultRead,
        };
    }

    public Task<bool> DeleteMachineAccessAsync(string organization, string id)
    {
        throw new NotImplementedException();
    }
}
