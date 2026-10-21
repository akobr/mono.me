using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Security;

// A Storyteller token is only a signature over the machine id, its project and scopes. Every call also reads the
// stored machine, so deleting it cuts the machine off at once, and a token signed with a stolen key cannot act as a
// machine of another kind. Its scopes can only narrow the stored ones: every operation checks its scopes stored on the
// machine and carried by the token (HttpRequestDataExtensions), so a carried scope that is not stored grants nothing.
// The project policy is MachineCredentialPolicy's part.
public static class FederatedMachineAccess
{
    public static bool IsFederated(FunctionContext context)
    {
        return context.Items.TryGetValue(FunctionContextItemKeys.MachineCredentialKind, out var value)
            && value is MachineCredentialKind.Federated;
    }

    // Returns the stored machine, so the caller reads it once.
    public static async Task<MachineAccess> EnsureAllowedAsync(
        HttpRequestData request,
        IAccessService accessService,
        string organization,
        string project,
        string machineId)
    {
        var items = request.FunctionContext.Items;

        // Checked before any read: a token of another project never costs a point read here.
        if (!IsItem(items, FunctionContextItemKeys.MachineOrganization, organization)
            || !IsItem(items, FunctionContextItemKeys.MachineProject, project))
        {
            throw new SecurityTokenException($"The Storyteller token of the machine {machineId} was not issued for the project {organization}.{project}.");
        }

        var machine = await accessService.GetMachineAccessAsync(organization, project, machineId);

        if (machine is null)
        {
            throw new SecurityTokenException($"The machine {machineId} doesn't exist in the project {organization}.{project}.");
        }

        if (machine.CredentialKind != MachineCredentialKind.Federated)
        {
            throw new SecurityTokenException($"The machine {machineId} of the project {organization}.{project} is {machine.CredentialKind:G}, not Federated.");
        }

        return machine;
    }

    private static bool IsItem(IDictionary<object, object> items, string key, string expected)
    {
        return items.TryGetValue(key, out var value)
            && value is string actual
            && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
