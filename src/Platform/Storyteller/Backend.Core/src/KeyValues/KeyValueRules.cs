using System.Text;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.KeyValues;

// A key is a binding path (identifier segments joined by '.'), so every key can be written as @key in a
// configuration. Keys keep their casing but are unique case-insensitively; the normalized form is the id.
public static partial class KeyValueRules
{
    public const int MaxKeyLength = 128;

    public const int MaxValueBytes = 24_576;

    public const int MaxClientEncryptedValueLength = 49_152;

    public const int MaxContentTypeLength = 255;

    public const string KeyPattern = @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z";

    public static bool IsValidKey(string? key)
    {
        return key is not null
            && key.Length <= MaxKeyLength
            && KeyRegex().IsMatch(key);
    }

    public static string EnsureKey(string? key)
    {
        if (!IsValidKey(key))
        {
            throw new InvalidInputException(
                $"The key '{key}' is not valid: use segments of letters, digits and '_' that start with a letter or '_', joined by '.', at most {MaxKeyLength} characters.",
                ErrorCodes.InvalidKey);
        }

        return key!;
    }

    public static string Normalize(string key)
    {
        return key.ToLowerInvariant();
    }

    public static void EnsureValue(string? value, KeyValueProtection protection)
    {
        if (value is null)
        {
            throw new InvalidInputException("The value is required.", ErrorCodes.ValueTooLarge);
        }

        if (protection == KeyValueProtection.Hyok)
        {
            if (value.Length > MaxClientEncryptedValueLength)
            {
                throw new InvalidInputException(
                    $"A client-encrypted value may have at most {MaxClientEncryptedValueLength} characters.",
                    ErrorCodes.ValueTooLarge);
            }

            return;
        }

        if (Encoding.UTF8.GetByteCount(value) > MaxValueBytes)
        {
            throw new InvalidInputException(
                $"A value may have at most {MaxValueBytes} bytes in UTF-8.",
                ErrorCodes.ValueTooLarge);
        }
    }

    public static void EnsureContentType(string? contentType)
    {
        if (contentType is not null && contentType.Length > MaxContentTypeLength)
        {
            throw new InvalidInputException(
                $"A content type may have at most {MaxContentTypeLength} characters.",
                ErrorCodes.InvalidKey);
        }
    }

    [GeneratedRegex(KeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
}
