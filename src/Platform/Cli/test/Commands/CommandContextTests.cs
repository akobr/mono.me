using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.UnitTests.Authentication;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class CommandContextTests
{
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.API_URL] = "https://storyteller.example.com/api" };
    private readonly AccessDefaultOptions _file = new() { OrganizationName = "house", ProjectName = "stored", ViewName = "staging" };

    [Fact]
    public void User_TargetsTheProjectOfAccessDefaults()
    {
        var context = CreateContext();

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("house", "stored", "staging"));
        context.IsProjectKnown.ShouldBeTrue();
        context.TrySetExplicitTarget(null, null);
    }

    [Fact]
    public void User_WithoutAccessDefaults_FallsBackToHouseMain()
    {
        var context = CreateContext(new AccessDefaultOptions());

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("house", "main", "default"));
    }

    [Fact]
    public void EnvironmentProject_WinsOverTheFileAndDropsItsView()
    {
        _environment[StorytellerVariables.PROJECT] = "acme.billing";

        var context = CreateContext();

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("acme", "billing", "default"));
    }

    [Fact]
    public void EnvironmentView_WinsOverTheFileView()
    {
        _environment[StorytellerVariables.VIEW] = "preview";

        var context = CreateContext();

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("house", "stored", "preview"));
    }

    [Fact]
    public void ExplicitTarget_WinsOverTheEnvironment()
    {
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        _environment[StorytellerVariables.VIEW] = "preview";
        var context = CreateContext();

        context.TrySetExplicitTarget("acme.web", "release");

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("acme", "web", "release"));
    }

    [Theory]
    [InlineData("acme.web", "default")]
    [InlineData("HOUSE.stored", "staging")]
    public void ExplicitProject_KeepsTheFileViewOnlyForTheFileProject(string projectKey, string view)
    {
        var context = CreateContext();

        context.TrySetExplicitTarget(projectKey, null);

        context.ViewName.ShouldBe(view);
    }

    [Fact]
    public void MalformedEnvironmentProject_IsReportedUnlessAProjectIsPassed()
    {
        _environment[StorytellerVariables.PROJECT] = "billing";
        var context = CreateContext();

        var exception = Should.Throw<AuthenticationException>(() => context.TrySetExplicitTarget(null, null));
        context.TrySetExplicitTarget("acme.web", null);

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("STORYTELLER_PROJECT must be <organization>.<project>.");
        (context.OrganizationName, context.ProjectName).ShouldBe(("acme", "web"));
    }

    [Fact]
    public void Machine_TargetsTheProjectOfItsKeyAndNeverTheFile()
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        var context = CreateContext();
        context.TrySetExplicitTarget(null, null);

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("acme", "billing", "default"));
        context.IsProjectKnown.ShouldBeTrue();
    }

    [Fact]
    public void Machine_WithoutAProject_IsStoppedBeforeAnyCall()
    {
        UseClientCredentials();
        var context = CreateContext();

        context.IsProjectKnown.ShouldBeFalse();
        var exception = Should.Throw<AuthenticationException>(() => context.TrySetExplicitTarget(null, "preview"));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("No project is set for the machine: set STORYTELLER_PROJECT=<organization>.<project> or pass -p.");
    }

    [Fact]
    public void Machine_WithAProjectFromTheEnvironmentOrAnOption_IsKnown()
    {
        UseClientCredentials();
        var passed = CreateContext();
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        var configured = CreateContext();

        passed.TrySetExplicitTarget("acme.web", null);
        configured.TrySetExplicitTarget(null, null);

        (passed.OrganizationName, passed.ProjectName, passed.IsProjectKnown).ShouldBe(("acme", "web", true));
        (configured.OrganizationName, configured.ProjectName, configured.IsProjectKnown).ShouldBe(("acme", "billing", true));
    }

    [Theory]
    [InlineData("acme.web", null)]
    [InlineData(null, "acme.web")]
    public void Machine_TargetingAnotherProjectThanItsKey_IsStopped(string? environmentProject, string? option)
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        if (environmentProject is not null)
        {
            _environment[StorytellerVariables.PROJECT] = environmentProject;
        }

        var exception = Should.Throw<AuthenticationException>(() => CreateContext().TrySetExplicitTarget(option, null));

        exception.Message.ShouldBe("The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web.");
    }

    [Fact]
    public void Machine_WithInvalidVariables_ReportsThemRatherThanTheMissingProject()
    {
        _environment.Remove(StorytellerVariables.API_URL);
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        var exception = Should.Throw<AuthenticationException>(() => CreateContext().TrySetExplicitTarget(null, null));

        exception.Message.ShouldBe("Set STORYTELLER_API_URL to the API base URL, for example https://<host>/api.");
    }

    private void UseClientCredentials()
    {
        _environment[StorytellerVariables.CLIENT_ID] = "client_01";
        _environment[StorytellerVariables.CLIENT_SECRET] = "secret";
        _environment[StorytellerVariables.TOKEN_URL] = "https://auth.example.com/oauth2/token";
    }

    private CommandContext CreateContext(AccessDefaultOptions? file = null)
    {
        var settings = MachineSignInSettings.FromEnvironment(name => _environment.GetValueOrDefault(name));
        var machine = new MachineAuthenticationService(
            settings,
            new MockFileSystem(),
            new StubHttpClientFactory(new ScriptedHttpHandler()),
            Mock.Of<IAuthenticationConfigurationResolver>());
        return new CommandContext(Options.Create(file ?? _file), machine);
    }
}
