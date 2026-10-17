using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Security;

// A bearer token is checked against the policy of the project the request resolves (CheckAccessToAsync): an
// identity provider machine token only where ClientCredentials is enabled, a Storyteller token only where Federated
// is. API key and certificate machines are checked by MachineAuthenticationMiddleware.
public static class MachineCredentialPolicy
{
    public static async Task EnsureAllowedAsync(FunctionContext context, string organization, string project)
    {
        if (!context.Items.TryGetValue(FunctionContextItemKeys.MachineCredentialKind, out var value)
            || value is not MachineCredentialKind kind)
        {
            return;
        }

        var policyStore = context.InstanceServices.GetService<IMachineAuthenticationPolicyStore>();
        var options = context.InstanceServices.GetService<IOptions<MachineAuthenticationOptions>>()?.Value ?? new MachineAuthenticationOptions();
        var stored = policyStore is null ? null : await policyStore.GetAsync(organization, project);
        var policy = MachineAuthenticationPolicies.Resolve(stored, options);

        if (!policy.IsEnabled(kind))
        {
            throw new SecurityTokenException(
                $"The project {organization}.{project} does not enable {Describe(kind)} for machines ({string.Join(", ", policy.EnabledCredentialKinds!)}).");
        }
    }

    private static string Describe(MachineCredentialKind kind)
    {
        return kind switch
        {
            MachineCredentialKind.ClientCredentials => "client credentials",
            MachineCredentialKind.Federated => "workload identity federation",
            _ => kind.ToString("G"),
        };
    }
}
