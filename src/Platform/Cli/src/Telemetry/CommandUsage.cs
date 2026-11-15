using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Telemetry;

// How a run used the tool, for its reports: the command path and the names of the options given ("sform storyteller kv
// set --secret --projectKey"). Both come from the model, never from the tokens typed, so no value can reach them.
internal sealed partial record CommandUsage(string Command, IReadOnlyList<string> Options)
{
    public static readonly CommandUsage Unknown = new(CommandPath.UNKNOWN, []);

    // The command and then each parent, in the order of the model. Read it in the root's OnParsingComplete or on a parse
    // that failed: later, the attribute model writes the default of a property into the Values of its option.
    public static CommandUsage Of(CommandLineApplication? command)
    {
        if (command is null)
        {
            return Unknown;
        }

        try
        {
            var names = new List<string>();

            for (var current = command; current is not null; current = current.Parent)
            {
                foreach (var option in current.Options)
                {
                    if (IsGiven(option) && NameOf(option) is { } name && !names.Contains(name, StringComparer.Ordinal))
                    {
                        names.Add(name);
                    }
                }
            }

            return new CommandUsage(CommandPath.Of(command), names.AsReadOnly());
        }
        catch (Exception)
        {
            return Unknown;
        }
    }

    public override string ToString()
    {
        return Options.Count == 0 ? Command : $"{Command} {string.Join(' ', Options)}";
    }

    // Values holds the tokens given; with none, it holds the DefaultValue, which a parse of the model leaves behind.
    internal static bool IsGiven(CommandOption option)
    {
        var values = option.Values;

        return values.Count > 0
            && !(values.Count == 1 && option.DefaultValue is not null && string.Equals(values[0], option.DefaultValue, StringComparison.Ordinal));
    }

    private static string? NameOf(CommandOption option)
    {
        var name = !string.IsNullOrEmpty(option.LongName)
            ? $"--{option.LongName}"
            : !string.IsNullOrEmpty(option.ShortName)
                ? $"-{option.ShortName}"
                : !string.IsNullOrEmpty(option.SymbolName)
                    ? $"-{option.SymbolName}"
                    : null;

        return name is not null && SafeName().IsMatch(name) ? name : null;
    }

    [GeneratedRegex("^--?[A-Za-z0-9?][A-Za-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
