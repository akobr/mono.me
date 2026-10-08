using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

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

        if (options.Provider == IdentityProviderKind.AuthKit)
        {
            return ValidateAuthKit(options.AuthKit);
        }

        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateAuthKit(AuthKitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:ClientId is required when Auth:Provider is AuthKit.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:Issuer is required when Auth:Provider is AuthKit.");
        }

        // The key retriever only fetches over HTTPS. Failing here beats a 503 on every request.
        if (!Uri.TryCreate(options.GetJwksUri(), UriKind.Absolute, out var jwksUri)
            || jwksUri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Fail("Auth:AuthKit:JwksUri (or Auth:AuthKit:ApiBaseUrl when JwksUri is empty) must be an absolute HTTPS URL.");
        }

        return ValidateOptionsResult.Success;
    }
}
