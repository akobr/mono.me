using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public sealed class KeycloakOptionsValidator : IValidateOptions<KeycloakOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out _))
        {
            failures.Add("Keycloak:ServerUrl must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Realm))
        {
            failures.Add("Keycloak:Realm is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Keycloak:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminClientSecret)
            && (string.IsNullOrWhiteSpace(options.AdminUsername) || string.IsNullOrWhiteSpace(options.AdminPassword)))
        {
            failures.Add("Keycloak:AdminClientSecret, or Keycloak:AdminUsername with Keycloak:AdminPassword, is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
