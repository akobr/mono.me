using _42.Testing.UI.Automation.Runtime.Reporting;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Reporting;

public sealed class RunEnvironmentTests
{
    [Fact]
    public void FromVariables_ReadsAGitHubActionsJob()
    {
        var variables = new Dictionary<string, string>
        {
            ["GITHUB_REF_NAME"] = "42/merge",
            ["GITHUB_HEAD_REF"] = "feat/424-133",
            ["GITHUB_SHA"] = "268a67a2e4f1c0de",
            ["GITHUB_RUN_NUMBER"] = "128",
            ["GITHUB_SERVER_URL"] = "https://github.com/",
            ["GITHUB_REPOSITORY"] = "akobr/mono.me",
            ["GITHUB_RUN_ID"] = "987654",
        };

        var environment = RunEnvironment.FromVariables(name => variables.GetValueOrDefault(name), "runner");

        environment.ShouldBe(new RunEnvironment("feat/424-133", "268a67a2e4f1c0de", "128", "https://github.com/akobr/mono.me/actions/runs/987654", "runner"));
    }

    [Fact]
    public void FromVariables_KnowsOnlyTheMachine_Locally()
    {
        var environment = RunEnvironment.FromVariables(_ => null, "laptop");

        environment.ShouldBe(new RunEnvironment(null, null, null, null, "laptop"));
    }

    [Fact]
    public void FromVariables_TakesTheRefName_OutsideAPullRequest()
    {
        var environment = RunEnvironment.FromVariables(name => name switch { "GITHUB_REF_NAME" => "main", "GITHUB_HEAD_REF" => " ", _ => null }, "runner");

        environment.Branch.ShouldBe("main");
    }
}
