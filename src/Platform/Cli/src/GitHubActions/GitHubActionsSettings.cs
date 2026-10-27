using System;
using System.Security.Cryptography;

namespace _42.Platform.Cli.GitHubActions;

// What sform needs to write into the environment of a GitHub Actions job, read once per process: the GITHUB_ENV file
// that the runner reads after the step, and the source of the random heredoc delimiters. Empty counts as unset.
public sealed class GitHubActionsSettings
{
    public const string ENV = "GITHUB_ENV";

    private const string DelimiterPrefix = "ghadelimiter_";

    private GitHubActionsSettings(string? environmentFile, Func<string> newDelimiter)
    {
        EnvironmentFile = environmentFile;
        NewDelimiter = newDelimiter;
    }

    // The path of GITHUB_ENV as the runner set it; null outside a job.
    public string? EnvironmentFile { get; }

    // A new delimiter for every call: ghadelimiter_ and 32 hex digits, as the runner's own.
    public Func<string> NewDelimiter { get; }

    public static GitHubActionsSettings FromEnvironment(Func<string, string?> getVariable, Func<string>? newDelimiter = null)
    {
        var environmentFile = getVariable(ENV);

        return new GitHubActionsSettings(
            string.IsNullOrWhiteSpace(environmentFile) ? null : environmentFile,
            newDelimiter ?? NewRandomDelimiter);
    }

    private static string NewRandomDelimiter()
    {
        return DelimiterPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    }
}
