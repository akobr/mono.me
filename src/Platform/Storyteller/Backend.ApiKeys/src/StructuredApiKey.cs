using System.Text;

namespace _42.Platform.Storyteller;

public record StructuredApiKey(string Organization, string Project, string MachineAccessId, string Secret)
{
    private const string Prefix = "2s";
    private const char Separator = '.';
    private const char MetadataSeparator = ':';

    public static StructuredApiKey? TryParse(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        var parts = rawKey.Split(Separator, 3);

        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return null;
        }

        var metadataSegment = parts[1];
        var secret = parts[2];

        if (string.IsNullOrEmpty(metadataSegment) || string.IsNullOrEmpty(secret))
        {
            return null;
        }

        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(Base64UrlDecode(metadataSegment));
        }
        catch
        {
            return null;
        }

        var metadata = decoded.Split(MetadataSeparator, 3);

        if (metadata.Length != 3
            || string.IsNullOrEmpty(metadata[0])
            || string.IsNullOrEmpty(metadata[1])
            || string.IsNullOrEmpty(metadata[2]))
        {
            return null;
        }

        return new StructuredApiKey(metadata[0], metadata[1], metadata[2], secret);
    }

    public string Format()
    {
        var metadata = $"{Organization}{MetadataSeparator}{Project}{MetadataSeparator}{MachineAccessId}";
        var encodedMetadata = Base64UrlEncode(Encoding.UTF8.GetBytes(metadata));
        return $"{Prefix}{Separator}{encodedMetadata}{Separator}{Secret}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
