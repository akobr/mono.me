using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using _42.Platform.Cli.Configuration;

namespace _42.Platform.Cli.Services;

// Opens the sign-in page of `sform account --browser`, without a shell:
// - SFORM_BROWSER: that program, with the address as its only argument. The value is the program alone (a name on the
//   PATH or a path, spaces allowed), never split into arguments; a program that needs arguments goes into a small script.
//   No fallback: a program that cannot be started is reported.
// - else BROWSER, as other tools read it: a list separated by the path separator (':' on Linux and macOS, ';' on
//   Windows); an entry with %s is a command line whose %s becomes the address (and %% a literal %), any other entry is a
//   program that gets the address as its only argument. The entries are tried in order, then the default browser.
// - else the address goes to the shell's default browser, as before.
public sealed class BrowserLauncher : IBrowserLauncher
{
    private const string AddressPlaceholder = "%s";

    private readonly SformEnvironment _environment;
    private readonly Func<ProcessStartInfo, IDisposable?> _start;

    public BrowserLauncher(SformEnvironment environment)
        : this(environment, Process.Start)
    {
    }

    internal BrowserLauncher(SformEnvironment environment, Func<ProcessStartInfo, IDisposable?> start)
    {
        _environment = environment;
        _start = start;
    }

    public bool TryOpen(Uri address)
    {
        foreach (var startInfo in CreateStartInfos(address))
        {
            try
            {
                // The process is not waited for: a browser may keep running after the sign-in.
                using var process = _start(startInfo);
                return true;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
            {
                // The next one, if any.
            }
        }

        return false;
    }

    // The starts to try, in order.
    internal IReadOnlyList<ProcessStartInfo> CreateStartInfos(Uri address)
    {
        var url = address.AbsoluteUri;

        return (_environment.BrowserVariable, _environment.BrowserCommand) switch
        {
            (SformVariables.BROWSER, { } program) => [CreateProgram(program, [url])],
            (SformVariables.CONVENTIONAL_BROWSER, { } list) => [.. ParseConventionalList(list, url), CreateShellDefault(url)],
            _ => [CreateShellDefault(url)],
        };
    }

    private static IEnumerable<ProcessStartInfo> ParseConventionalList(string list, string url)
    {
        foreach (var entry in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!entry.Contains(AddressPlaceholder, StringComparison.Ordinal))
            {
                yield return CreateProgram(entry, [url]);
                continue;
            }

            var parts = SplitCommandLine(entry);

            if (parts.Count > 0)
            {
                yield return CreateProgram(parts[0], parts.Skip(1).Select(part => Substitute(part, url)));
            }
        }
    }

    private static ProcessStartInfo CreateProgram(string program, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(program) { UseShellExecute = false };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateShellDefault(string url)
    {
        return new ProcessStartInfo(url) { UseShellExecute = true };
    }

    // %s becomes the address and %% a literal percent sign, read left to right as Python's webbrowser reads BROWSER.
    internal static string Substitute(string part, string url)
    {
        var result = new System.Text.StringBuilder(part.Length + url.Length);

        for (var index = 0; index < part.Length; index++)
        {
            if (part[index] == '%' && index + 1 < part.Length && part[index + 1] is 's' or '%')
            {
                result.Append(part[index + 1] == 's' ? url : "%");
                index++;
                continue;
            }

            result.Append(part[index]);
        }

        return result.ToString();
    }

    // Splits at white space; single or double quotes keep a part together.
    private static List<string> SplitCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var hasPart = false;
        char? quote = null;

        foreach (var character in commandLine)
        {
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Append(character);
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
                hasPart = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (hasPart)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    hasPart = false;
                }
            }
            else
            {
                current.Append(character);
                hasPart = true;
            }
        }

        if (hasPart)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }
}
