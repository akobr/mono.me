using System;
using System.Collections.Generic;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.KeyValues;

// The protection levels as the user types them, and their conversion to the per-property enums of the generated client.
public static class KeyValueLevels
{
    public const string Mask = "••••••";

    public static readonly IReadOnlyList<string> Names = Enum.GetNames<KeyValueProtection>();

    public static bool TryParse(string? text, out KeyValueProtection level)
    {
        if (!string.IsNullOrWhiteSpace(text)
            && Enum.TryParse(text.Trim(), ignoreCase: true, out level)
            && Enum.IsDefined(level))
        {
            return true;
        }

        level = default;
        return false;
    }

    public static KeyValueProtection Parse(IExtendedConsole console, string? text)
    {
        if (TryParse(text, out var level))
        {
            return level;
        }

        var message = $"Unknown protection level '{text}'. Known levels: {string.Join(", ", Names.Select(name => name.ToLowerInvariant()))}.";
        console.WriteImportant(message);
        throw new WrongInputException(message);
    }

    // The same member of another generated enum (KeyValueWriteProtection, KeyValueBulkRequestProtection, ...).
    public static TEnum To<TEnum>(KeyValueProtection level)
        where TEnum : struct, Enum
    {
        return Enum.Parse<TEnum>(level.ToString());
    }

    public static bool IsSecret(Enum? level)
    {
        return level is not null && !string.Equals(level.ToString(), nameof(KeyValueProtection.Public), StringComparison.Ordinal);
    }

    public static bool IsClientEncrypted(Enum? level)
    {
        return level is not null && string.Equals(level.ToString(), nameof(KeyValueProtection.Hyok), StringComparison.Ordinal);
    }
}
