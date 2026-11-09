using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace _42.Platform.Cli.Configuration;

// The SFORM_<section>__<key> variables win over every configuration file (Startup.AddEnvironmentVariables), so anything
// that sets the environment of a run decides where sform sends its requests and the user's token. sform says so on
// standard error whenever a variable sets the address of the API (general:baseUrl) or a sign-in setting
// (authentication:*), once per run, before the command's own output.
public static class ConfigurationOverrideNotice
{
    private const string BaseUrlKey = "general:baseUrl";
    private const string AuthenticationSection = "authentication:";

    // The variables of the environment that set the API address or a sign-in setting, by their configuration key.
    public static IReadOnlyList<(string Variable, string Key, string Value)> Find(IDictionary variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var found = new List<(string Variable, string Key, string Value)>();

        foreach (DictionaryEntry entry in variables)
        {
            if (entry.Key is not string name
                || !name.StartsWith(SformVariables.CONFIGURATION_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // As the environment variables provider reads them: the prefix removed, __ for the separator.
            var key = name[SformVariables.CONFIGURATION_PREFIX.Length..].Replace("__", ":", StringComparison.Ordinal);

            if (string.Equals(key, BaseUrlKey, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(AuthenticationSection, StringComparison.OrdinalIgnoreCase))
            {
                found.Add((name, key, entry.Value as string ?? string.Empty));
            }
        }

        return found.OrderBy(item => item.Variable, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // The address of the API with its value, the sign-in settings by name only.
    public static void Write(IDictionary variables, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var found = Find(variables);
        var baseUrl = found.FirstOrDefault(item => string.Equals(item.Key, BaseUrlKey, StringComparison.OrdinalIgnoreCase));

        if (baseUrl.Variable is not null)
        {
            error.WriteLine($"! The API address comes from the environment, {baseUrl.Variable}={baseUrl.Value}; sform sends your sign-in there.");
        }

        var authentication = found.Where(item => item.Key.StartsWith(AuthenticationSection, StringComparison.OrdinalIgnoreCase)).Select(item => item.Variable).ToList();

        if (authentication.Count > 0)
        {
            error.WriteLine($"! The sign-in settings come from the environment: {string.Join(", ", authentication)}.");
        }

        error.Flush();
    }
}
