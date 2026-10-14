using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Security;

// A bearer token does not name its project, so the project policy is checked once the request
// resolves one (CheckAccessToAsync): an identity provider machine token is accepted only by a
// project that enables ClientCredentials. API key and certificate machines are checked by
// MachineAuthenticationMiddleware.
public static class MachineCredentialPolicy
{
    public static async Task EnsureAllowedAsync(FunctionContext context, string organization, string project)
    {
        if (!context.Items.TryGetValue(FunctionContextItemKeys.MachineCredentialKind, out var value)
            || value is not MachineCredentialKind.ClientCredentials)
        {
            return;
        }

        var policyStore = context.InstanceServices.GetService<IMachineAuthenticationPolicyStore>();
        var options = context.InstanceServices.GetService<IOptions<MachineAuthenticationOptions>>()?.Value ?? new MachineAuthenticationOptions();
        var stored = policyStore is null ? null : await policyStore.GetAsync(organization, project);
        var policy = MachineAuthenticationPolicies.Resolve(stored, options);

        if (!policy.IsEnabled(MachineCredentialKind.ClientCredentials))
        {
            throw new SecurityTokenException(
                $"The project {organization}.{project} does not enable client credentials for machines ({string.Join(", ", policy.EnabledCredentialKinds!)}).");
        }
    }
}
