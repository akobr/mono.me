using System;
using System.Text;

namespace _42.Platform.Cli.Authentication;

// What a structured machine API key tells about its machine: 2s.{base64url("organization:project:machineId")}.{secret}.
// A copy of StructuredApiKey.TryParse (Storyteller/Access.ApiKeys), which depends on Backend.Core.
// The secret is not kept, so the record can be printed.
public sealed record MachineApiKey(string Organization, string Project, string MachineId)
{
    private const string Prefix = "2s";
    private const char Separator = '.';
    private const char MetadataSeparator = ':';

    public static MachineApiKey? TryParse(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        var parts = rawKey.Split(Separator, 3);

        return parts.Length == 3 && parts[0] == Prefix && parts[2].Length > 0
            ? TryParseMachine(parts[1])
            : null;
    }

    // The machine segment alone, base64url("organization:project:machineId"), as StorytellerCredentialVariables.ApiKeyMachine
    // gives it without the secret.
    public static MachineApiKey? TryParseMachine(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return null;
        }

        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(DecodeBase64Url(segment));
        }
        catch (FormatException)
        {
            return null;
        }

        var metadata = decoded.Split(MetadataSeparator, 3);

        return metadata.Length == 3 && metadata[0].Length > 0 && metadata[1].Length > 0 && metadata[2].Length > 0
            ? new MachineApiKey(metadata[0], metadata[1], metadata[2])
            : null;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}
