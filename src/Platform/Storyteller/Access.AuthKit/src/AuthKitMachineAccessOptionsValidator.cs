using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Registered only with AddAuthKitMachineAccess: M2M applications need the management key,
// the AuthKit domain that issues their tokens, the environment client ID they are issued for
// and the organization that owns them.
public sealed class AuthKitMachineAccessOptionsValidator : IValidateOptions<UserAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, UserAuthenticationOptions options)
    {
        var authKit = options.AuthKit;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(authKit.ClientId))
        {
            failures.Add("Auth:AuthKit:ClientId (the M2M token audience) is required for AuthKit machine access.");
        }

        if (string.IsNullOrWhiteSpace(authKit.ApiKey))
        {
            failures.Add("Auth:AuthKit:ApiKey is required for AuthKit machine access.");
        }

        if (string.IsNullOrWhiteSpace(authKit.MachineOrganizationId))
        {
            failures.Add("Auth:AuthKit:MachineOrganizationId is required for AuthKit machine access.");
        }

        if (!Uri.TryCreate(authKit.AuthKitDomain, UriKind.Absolute, out var domain) || domain.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Auth:AuthKit:AuthKitDomain must be an absolute HTTPS URL for AuthKit machine access.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
