using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Security;

// A Storyteller token is only a signature over the machine id, its project and scopes. Every call also reads the
// stored machine, so deleting it cuts the machine off at once, and a token signed with a stolen key can neither act
// as a machine of another kind nor exceed the stored scope. The project policy is MachineCredentialPolicy's part.
public static class FederatedMachineAccess
{
    public static bool IsFederated(FunctionContext context)
    {
        return context.Items.TryGetValue(FunctionContextItemKeys.MachineCredentialKind, out var value)
            && value is MachineCredentialKind.Federated;
    }

    public static async Task EnsureAllowedAsync(
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

        var granted = MachineAccessScopes.Get(machine.Scope);
        var excess = request.GetScopes()
            .Where(scope => !granted.Contains(scope, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (excess.Count > 0)
        {
            throw new SecurityTokenException($"The Storyteller token of the machine {machineId} carries scopes the machine doesn't have ({string.Join(", ", excess)}).");
        }
    }

    private static bool IsItem(IDictionary<object, object> items, string key, string expected)
    {
        return items.TryGetValue(key, out var value)
            && value is string actual
            && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
