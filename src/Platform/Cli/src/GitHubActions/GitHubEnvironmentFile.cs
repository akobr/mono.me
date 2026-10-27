using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using _42.Platform.Storyteller.ConfigurationFormats;

namespace _42.Platform.Cli.GitHubActions;

// The variables sform appends to GITHUB_ENV. Each reaches every later step of the job, third-party actions included,
// and shows in the env block of their logs. The names come from the authors of the configuration, so names that steer
// the runner or the tools of later steps are refused, and so are values the file or the platform cannot carry. No list
// of such names is complete (MSBuild reads every variable), which is why sform asks for a prefix.
internal static class GitHubEnvironmentFile
{
    // NAME=value, the limit of Windows, below the 128 KiB of Linux.
    public const int MaxVariableLength = 32_767;

    public const int MaxTotalBytes = 512 * 1024;

    private static readonly FrozenSet<string> ReservedNames = new[]
    {
        "NODE_OPTIONS", "PATH", "HOME", "SHELL", "ENV", "BASH_ENV", "CDPATH", "IFS", "PS4", "PROMPT_COMMAND", "SHELLOPTS",
        "BASHOPTS", "GLOBIGNORE", "CLASSPATH", "CC", "CXX", "MAKEFLAGS", "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS",
        "JDK_JAVA_OPTIONS", "JAVA_OPTS", "MAVEN_OPTS", "GRADLE_OPTS", "SBT_OPTS", "PYTHONPATH", "PYTHONSTARTUP",
        "PYTHONHOME", "PERL5OPT", "PERL5LIB", "PERLLIB", "RUBYOPT", "RUBYLIB", "NODE_PATH", "NODE_EXTRA_CA_CERTS",
        "NODE_TLS_REJECT_UNAUTHORIZED", "LUA_INIT", "RUSTC_WRAPPER", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE", "GOFLAGS", "GOPROXY", "GONOSUMDB",
        "GOSUMDB", "GOINSECURE", "RESTORESOURCES", "KUBECONFIG", "PATHEXT", "COMSPEC", "PSMODULEPATH",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ReservedPrefixes =
    [
        "GITHUB_", "RUNNER_", "ACTIONS_", "INPUT_", "STATE_", "LD_", "DYLD_", "BASH_FUNC_", "DOTNET_", "COREHOST_",
        "CORECLR_", "COMPLUS_", "MSBUILD", "NUGET_", "NPM_CONFIG_", "YARN_", "PIP_", "CARGO_", "DOCKER_", "GIT_",
        "STORYTELLER_",
    ];

    // The names compare without regard to case, prefix included.
    public static bool IsReserved(string name)
    {
        return ReservedNames.Contains(name)
            || ReservedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // Why nothing may be exported, by names and paths only; empty when the variables can be written.
    public static IReadOnlyList<string> FindRefusals(IReadOnlyList<EnvironmentVariable> variables)
    {
        var refusals = new List<string>();
        var reserved = variables.Where(variable => IsReserved(variable.Name)).ToList();

        if (reserved.Count > 0)
        {
            refusals.Add($"These names would change how the runner or the tools of later steps run, and are never exported: {Describe(reserved)}.");
        }

        var nul = variables.Where(variable => variable.Value.Contains('\0', StringComparison.Ordinal)).ToList();

        if (nul.Count > 0)
        {
            refusals.Add($"These values contain U+0000, which a variable cannot hold: {Describe(nul)}.");
        }

        var tooLong = variables.Where(variable => variable.Name.Length + 1 + variable.Value.Length > MaxVariableLength).ToList();

        if (tooLong.Count > 0)
        {
            refusals.Add($"These variables are longer than {MaxVariableLength.ToString("N0", CultureInfo.InvariantCulture)} characters as NAME=value, the limit of Windows: {Describe(tooLong)}.");
        }

        var total = variables.Sum(variable => (long)Encoding.UTF8.GetByteCount(variable.Name) + 1 + Encoding.UTF8.GetByteCount(variable.Value) + 1);

        if (total > MaxTotalBytes)
        {
            refusals.Add($"The variables take {total.ToString("N0", CultureInfo.InvariantCulture)} bytes as NAME=value lines, more than the {MaxTotalBytes / 1024} KiB sform exports.");
        }

        return refusals;
    }

    // NAME<<DELIMITER, the value and DELIMITER on lines of their own, for each variable: the one syntax that carries any
    // value. One delimiter serves every variable, taken again until no value contains it.
    public static string Build(IReadOnlyList<EnvironmentVariable> variables, Func<string> newDelimiter)
    {
        var delimiter = NewDelimiter(variables, newDelimiter);
        var builder = new StringBuilder();

        foreach (var variable in variables)
        {
            builder.Append(variable.Name).Append("<<").Append(delimiter).Append('\n')
                .Append(variable.Value).Append('\n')
                .Append(delimiter).Append('\n');
        }

        return builder.ToString();
    }

    private static string NewDelimiter(IReadOnlyList<EnvironmentVariable> variables, Func<string> newDelimiter)
    {
        // 128 random bits do not repeat; the bound only stops a source that always returns the same delimiter.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var delimiter = newDelimiter();

            if (delimiter.Length > 0 && !variables.Any(variable => variable.Value.Contains(delimiter, StringComparison.Ordinal)))
            {
                return delimiter;
            }
        }

        throw new InvalidOperationException("No heredoc delimiter that occurs in none of the values.");
    }

    // NAME ($.path), never a value.
    private static string Describe(IEnumerable<EnvironmentVariable> variables)
    {
        return string.Join(", ", variables.Select(variable => variable.ToString()));
    }
}
