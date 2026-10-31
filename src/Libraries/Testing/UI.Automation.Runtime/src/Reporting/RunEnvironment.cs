namespace _42.Testing.UI.Automation.Runtime.Reporting;

// Where the run happens: the git and build values of a GitHub Actions job (GITHUB_* variables) when present, and the
// machine. Locally only the machine is known.
public sealed record RunEnvironment(string? Branch, string? Sha, string? BuildNumber, string? BuildUrl, string Machine)
{
    public static RunEnvironment Current => FromVariables(Environment.GetEnvironmentVariable, Environment.MachineName);

    // Branch: GITHUB_HEAD_REF (the source branch of a pull request) or GITHUB_REF_NAME; BuildNumber: GITHUB_RUN_NUMBER;
    // BuildUrl: GITHUB_SERVER_URL/GITHUB_REPOSITORY/actions/runs/GITHUB_RUN_ID.
    public static RunEnvironment FromVariables(Func<string, string?> getVariable, string machine)
    {
        ArgumentNullException.ThrowIfNull(getVariable);

        string? Read(string name)
        {
            var value = getVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var server = Read("GITHUB_SERVER_URL");
        var repository = Read("GITHUB_REPOSITORY");
        var runId = Read("GITHUB_RUN_ID");

        return new RunEnvironment(
            Read("GITHUB_HEAD_REF") ?? Read("GITHUB_REF_NAME"),
            Read("GITHUB_SHA"),
            Read("GITHUB_RUN_NUMBER"),
            server is not null && repository is not null && runId is not null ? $"{server.TrimEnd('/')}/{repository}/actions/runs/{runId}" : null,
            machine);
    }
}
