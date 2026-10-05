using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Api.Security;

// Storyteller issues GUID machine IDs for API keys and certificates. AuthKit M2M machines are
// identified by their WorkOS application client ID, which is also the token subject.
public static partial class MachineIds
{
    public static bool IsValid(string? id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (Guid.TryParse(id, out _) || WorkOsClientId().IsMatch(id));
    }

    [GeneratedRegex("^client_[0-9A-Za-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkOsClientId();
}
