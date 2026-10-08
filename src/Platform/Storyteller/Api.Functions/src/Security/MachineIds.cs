using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Api.Security;

// Storyteller issues GUID machine IDs for API keys and certificates. AuthKit M2M machines are
// identified by their WorkOS application client ID, which is also the token subject. Keycloak
// machines use the client id 42.sform.{organization}.{project}.{guid:N}, which is also the token azp.
public static partial class MachineIds
{
    public static bool IsValid(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (Guid.TryParse(id, out _) || WorkOsClientId().IsMatch(id) || KeycloakClientId().IsMatch(id));
    }

    [GeneratedRegex("^client_[0-9A-Za-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkOsClientId();

    // Organization and project are one key segment each, so they contain no dots.
    [GeneratedRegex("^42\\.sform\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeycloakClientId();
}
