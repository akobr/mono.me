using System;
using System.Collections.Generic;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Telemetry;

// The values a run was given on the command line, which its reports must not show: the values of the options and the
// arguments of the model (response files included), and every token of args that is not a bare option name (the token
// of an unknown command or option, which the model never saw). Each value also gives its parts (acme.billing: acme and
// billing), so a message that names a part alone loses it too.
// Only the project and the view may go to the local log, an allow-list: an option is hidden unless it is listed.
internal sealed class CommandLineValues
{
    public static readonly CommandLineValues Empty = new([], []);

    // The long names of the options whose values the local log may keep.
    internal static readonly IReadOnlySet<string> LocalOptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "projectKey",
        "view",
        "viewName",
    };

    private const int MinimumPartLength = 4;

    private static readonly char[] PartSeparators = ['=', ',', ';', '.', '/', '\\'];

    private readonly IReadOnlyList<string> _values;

    private CommandLineValues(IReadOnlyList<string> values, IReadOnlyList<KeyValuePair<string, string>> localValues)
    {
        _values = values;
        LocalValues = localValues;

        var local = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in localValues.Select(pair => pair.Value))
        {
            local.UnionWith(WithParts(value));
        }

        Hidden = values.SelectMany(WithParts).Where(value => !local.Contains(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
        All = Hidden.Concat(local).Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }

    // The values of the listed options, by option name (--projectKey), for the local log only.
    public IReadOnlyList<KeyValuePair<string, string>> LocalValues { get; }

    // Every value and part but those of LocalValues: what neither the local log nor Sentry may show.
    public IReadOnlyList<string> Hidden { get; }

    // Hidden and LocalValues with their parts: what Sentry may not show.
    public IReadOnlyList<string> All { get; }

    // From the model: read it in the root's OnParsingComplete or on a parse that failed, as CommandUsage.
    public static CommandLineValues Of(CommandLineApplication? command)
    {
        if (command is null)
        {
            return Empty;
        }

        var values = new List<string>();
        var localValues = new List<KeyValuePair<string, string>>();

        try
        {
            for (var current = command; current is not null; current = current.Parent)
            {
                foreach (var option in current.Options)
                {
                    if (!CommandUsage.IsGiven(option))
                    {
                        continue;
                    }

                    foreach (var value in option.Values.OfType<string>().Where(value => value.Length > 0))
                    {
                        if (option.LongName is { } name && LocalOptions.Contains(name))
                        {
                            localValues.Add(new($"--{name}", value));
                        }
                        else
                        {
                            values.Add(value);
                        }
                    }
                }

                foreach (var argument in current.Arguments)
                {
                    values.AddRange(argument.Values.OfType<string>());
                }

                values.AddRange(current.RemainingArguments);
            }
        }
        catch (Exception)
        {
            // What was read so far is kept; args adds every token anyway.
        }

        return new CommandLineValues(Clean(values), localValues.AsReadOnly());
    }

    // From the tokens of Main: a plain token, the value of --name=value or --name:value, what follows the first letter of
    // a short option (-pacme.billing, or a cluster), the path of @file. A long option name alone (--secret) is no value.
    public static CommandLineValues FromArguments(IEnumerable<string>? args)
    {
        var values = new List<string>();

        foreach (var token in args ?? [])
        {
            if (string.IsNullOrEmpty(token) || token == "--")
            {
                continue;
            }

            if (token[0] == '-')
            {
                var separator = token.IndexOfAny(['=', ':']);

                if (separator > 0 && separator < token.Length - 1)
                {
                    values.Add(token[(separator + 1)..]);
                }

                if (!token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2 && token[2] is not ('=' or ':'))
                {
                    values.Add(token[2..]);
                }

                continue;
            }

            values.Add(token[0] == '@' && token.Length > 1 ? token[1..] : token);
        }

        return new CommandLineValues(Clean(values), []);
    }

    public CommandLineValues Merge(CommandLineValues other)
    {
        return new CommandLineValues(
            _values.Concat(other._values).Distinct(StringComparer.Ordinal).ToList().AsReadOnly(),
            LocalValues.Concat(other.LocalValues).Distinct().ToList().AsReadOnly());
    }

    // The value and its parts of four characters or more.
    internal static IEnumerable<string> WithParts(string value)
    {
        yield return value;

        foreach (var part in value.Split(PartSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length >= MinimumPartLength && !string.Equals(part, value, StringComparison.Ordinal))
            {
                yield return part;
            }
        }
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values)
    {
        return values
            .Select(value => value?.Trim())
            .OfType<string>()
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }
}
