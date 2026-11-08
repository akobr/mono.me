using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

// A preference of sform account settings: its key, where it lives in the settings file and which values it takes.
public sealed class UserSettingDefinition
{
    // The key on the command line, e.g. editor.type.
    public required string Key { get; init; }

    // The section of the settings file, e.g. editor.
    public required string Section { get; init; }

    // The properties of the section that the key writes, and that unset removes.
    public required IReadOnlyList<string> Properties { get; init; }

    public required string Description { get; init; }

    // How a value looks, shown when a value is refused.
    public required string ValueFormat { get; init; }

    // The values it takes; null for free text.
    public IReadOnlyList<string>? AllowedValues { get; init; }

    // The value the commands use when it is not set; null when there is none.
    public string? DefaultValue { get; init; }

    // What the list shows when it is not set.
    public required string DefaultDisplay { get; init; }

    // The properties to store for a value from the command line; null when the value is not valid.
    public required Func<string, IReadOnlyDictionary<string, JToken?>?> Parse { get; init; }

    // The value as the command line names it, from a normalised section; null when it is not set.
    public required Func<JObject, string?> Read { get; init; }
}
