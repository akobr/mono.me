namespace _42.Platform.Storyteller.Mcp.Tools;

// Enum values arrive as names. Enum.TryParse also takes numbers ("99") and returns an undefined value, so a name is
// matched against the defined names only: case-insensitive, with dots, dashes and underscores ignored, so the scope
// string Secrets.Read and sform's secrets-read name the same value as SecretsRead.
public static class EnumNames
{
    public static TEnum Parse<TEnum>(string? value, string parameter)
        where TEnum : struct, Enum
    {
        var wanted = Normalize(value);

        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (wanted.Length > 0 && string.Equals(Normalize(name), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        throw new ToolInputException($"'{parameter}' has the unknown value '{value}'; the values are {List<TEnum>()}.");
    }

    public static TEnum? ParseOptional<TEnum>(string? value, string parameter)
        where TEnum : struct, Enum
    {
        return string.IsNullOrWhiteSpace(value) ? null : Parse<TEnum>(value, parameter);
    }

    // The SDK generates one enum per property (KeyValueProtection, KeyValueWriteProtection, ...) with the same names.
    public static TTarget Convert<TTarget>(Enum value)
        where TTarget : struct, Enum
    {
        return Enum.Parse<TTarget>(value.ToString());
    }

    public static string List<TEnum>()
        where TEnum : struct, Enum
    {
        return Join(Enum.GetNames<TEnum>());
    }

    public static string Join(IReadOnlyList<string> names, string conjunction = "or")
    {
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} {conjunction} {names[^1]}",
        };
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().Replace(".", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
    }
}
