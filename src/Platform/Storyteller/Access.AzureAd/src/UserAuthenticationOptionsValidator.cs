using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public sealed class UserAuthenticationOptionsValidator : IValidateOptions<UserAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, UserAuthenticationOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            return ValidateOptionsResult.Fail($"Auth:Provider '{options.Provider}' is not a known identity provider.");
        }

        if (options.Provider == IdentityProviderKind.EntraId
            && (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.TenantId)))
        {
            return ValidateOptionsResult.Fail("Auth:ClientId and Auth:TenantId are required when Auth:Provider is EntraId.");
        }

        if (options.Provider == IdentityProviderKind.AuthKit
            && string.IsNullOrWhiteSpace(options.AuthKit.ClientId))
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:ClientId is required when Auth:Provider is AuthKit.");
        }

        return ValidateOptionsResult.Success;
    }
}
