using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Telemetry;

// The path of a command ("sform storyteller kv get") from the primary names of the command tree, never from the
// arguments: an alias like "story" or "login" reports its primary name, and a value never reaches the path.
internal static partial class CommandPath
{
    public const string UNKNOWN = "unknown";

    private const int MaxDepth = 8;

    public static string Of(CommandLineApplication? command)
    {
        try
        {
            var names = new List<string>(MaxDepth);

            for (var current = command; current is not null; current = current.Parent)
            {
                if (names.Count == MaxDepth || current.Name is not { } name || !SafeName().IsMatch(name))
                {
                    return UNKNOWN;
                }

                names.Add(name);
            }

            if (names.Count == 0)
            {
                return UNKNOWN;
            }

            names.Reverse();
            return string.Join(' ', names);
        }
        catch (Exception)
        {
            return UNKNOWN;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
