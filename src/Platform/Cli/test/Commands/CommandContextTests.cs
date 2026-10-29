using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class CommandContextTests
{
    private const string FederatedMachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string Key = "@acme.billing.staging.sbt.contoso";

    private readonly ScriptedHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.ENDPOINT] = "https://storyteller.example.com/api" };
    private readonly AccessDefaultOptions _file = new() { OrganizationName = "house", ProjectName = "stored", ViewName = "staging" };
    private MachineAuthenticationService? _machine;

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
        _environment[TargetVariables.PROJECT] = "acme.billing";

        var context = CreateContext();

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("acme", "billing", "default"));
    }

    [Fact]
    public void EnvironmentView_WinsOverTheFileView()
    {
        _environment[TargetVariables.VIEW] = "preview";

        var context = CreateContext();

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("house", "stored", "preview"));
    }

    [Fact]
    public void ExplicitTarget_WinsOverTheEnvironment()
    {
        _environment[TargetVariables.PROJECT] = "acme.billing";
        _environment[TargetVariables.VIEW] = "preview";
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
        _environment[TargetVariables.PROJECT] = "billing";
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
        exception.Message.ShouldBe("No project is set for the machine: set STORYTELLER_PROJECT=<organization>.<project>, set STORYTELLER_KEY to a full key of the project, or pass -p.");
    }

    [Fact]
    public void Machine_WithAProjectFromTheEnvironmentOrAnOption_IsKnown()
    {
        UseClientCredentials();
        var passed = CreateContext();
        _environment[TargetVariables.PROJECT] = "acme.billing";
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
            _environment[TargetVariables.PROJECT] = environmentProject;
        }

        var exception = Should.Throw<AuthenticationException>(() => CreateContext().TrySetExplicitTarget(option, null));

        exception.Message.ShouldBe("The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web.");
    }

    [Fact]
    public void Machine_WithInvalidVariables_ReportsThemRatherThanTheMissingProject()
    {
        _environment.Remove(StorytellerVariables.ENDPOINT);
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");

        var exception = Should.Throw<AuthenticationException>(() => CreateContext().TrySetExplicitTarget(null, null));

        exception.Message.ShouldBe("Set STORYTELLER_ENDPOINT to the API base URL, for example https://<host>/api.");
    }

    [Fact]
    public async Task FederatedMachine_IsBoundToTheOptionOverStorytellerProject()
    {
        _environment[TargetVariables.PROJECT] = "acme.billing";
        UseFederated();
        _http.Respond(HttpStatusCode.OK, """{"access_token":"st-web","token_type":"Bearer","expires_in":900}""");
        var context = CreateContext();

        context.TrySetExplicitTarget("acme.web", null);

        // The exchange goes to the token endpoint of the bound project.
        (await _machine!.GetAccessTokenAsync()).ShouldBe("st-web");
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://storyteller.example.com/api/v1/acme/web/access/token"));
    }

    [Fact]
    public async Task FederatedMachine_WithoutAProject_IsStoppedAndBoundToNothing()
    {
        UseFederated();
        var context = CreateContext();

        var exception = Should.Throw<AuthenticationException>(() => context.TrySetExplicitTarget(null, null));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("No project is set for the machine: set STORYTELLER_PROJECT=<organization>.<project>, set STORYTELLER_KEY to a full key of the project, or pass -p.");
        (await _machine!.GetAccessTokenAsync()).ShouldBeNull();
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void FederatedMachine_NeverTargetsTheProjectOrViewOfAccessDefaults()
    {
        UseFederated();

        var context = CreateContext();

        // The credential names no project, and access.default.json may be left over from another job.
        context.IsProjectKnown.ShouldBeFalse();
        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("house", "main", "default"));
    }

    [Fact]
    public void Machine_TargetsTheProjectAndViewOfStorytellerKey()
    {
        UseClientCredentials();
        _environment[StorytellerVariables.KEY] = Key;
        var context = CreateContext();

        context.TrySetExplicitTarget(null, null);

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("acme", "billing", "staging"));
        context.IsProjectKnown.ShouldBeTrue();
    }

    // STORYTELLER_PROJECT wins over the key, STORYTELLER_VIEW over its view; the view of the key belongs to its project.
    [Theory]
    [InlineData("acme.web", null, "acme", "web", "default")]
    [InlineData("ACME.Billing", null, "ACME", "Billing", "staging")]
    [InlineData("acme.web", "preview", "acme", "web", "preview")]
    [InlineData(null, "preview", "acme", "billing", "preview")]
    public void Machine_StorytellerProjectAndView_WinOverStorytellerKey(
        string? environmentProject,
        string? environmentView,
        string organization,
        string project,
        string view)
    {
        UseClientCredentials();
        _environment[StorytellerVariables.KEY] = Key;
        SetIfGiven(TargetVariables.PROJECT, environmentProject);
        SetIfGiven(TargetVariables.VIEW, environmentView);
        var context = CreateContext();

        context.TrySetExplicitTarget(null, null);

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe((organization, project, view));
    }

    [Theory]
    [InlineData(null, "acme.web", null, "default")]
    [InlineData(null, "ACME.Billing", null, "staging")]
    [InlineData(null, "acme.billing", "release", "release")]
    [InlineData("acme.web", "acme.billing", null, "staging")]
    public void Machine_ExplicitTarget_WinsOverStorytellerKey_WhoseViewFollowsItsProject(string? environmentProject, string projectKey, string? viewName, string view)
    {
        UseClientCredentials();
        _environment[StorytellerVariables.KEY] = Key;
        SetIfGiven(TargetVariables.PROJECT, environmentProject);
        var context = CreateContext();

        context.TrySetExplicitTarget(projectKey, viewName);

        context.ViewName.ShouldBe(view);
    }

    // As with STORYTELLER_PROJECT: the key's project must be the project of the credential, unless -p or
    // STORYTELLER_PROJECT names the target.
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("acme.billing", null, true)]
    [InlineData(null, "acme.billing", true)]
    public void Machine_WithAKeyOfAnotherProjectThanItsCredential_IsStoppedUnlessTheTargetIsNamed(string? environmentProject, string? option, bool passes)
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        _environment[StorytellerVariables.KEY] = "@acme.web.staging.sbt.contoso";
        SetIfGiven(TargetVariables.PROJECT, environmentProject);
        var context = CreateContext();

        if (passes)
        {
            context.TrySetExplicitTarget(option, null);
            (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("acme", "billing", "default"));
            return;
        }

        var exception = Should.Throw<AuthenticationException>(() => context.TrySetExplicitTarget(option, null));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web.");
    }

    // FullKey.TryParse, as the provider parses it: a machine stops at a key that is not a full key where the key would pick
    // the project; -p and STORYTELLER_PROJECT win over it.
    [Theory]
    [InlineData("acme.billing")]
    [InlineData("@acme.billing")]
    [InlineData("@acme.billing.default")]
    [InlineData("@acme.billing.default.xyz.contoso")]
    public void Machine_WithAnInvalidStorytellerKey_IsAConfigurationError(string key)
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        _environment[StorytellerVariables.KEY] = key;
        var context = CreateContext();

        var exception = Should.Throw<AuthenticationException>(() => context.TrySetExplicitTarget(null, null));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("STORYTELLER_KEY must be a full key, @{organization}.{project}.{view}.{annotation key}.");

        CreateContext().TrySetExplicitTarget("acme.billing", null);

        _environment[TargetVariables.PROJECT] = "acme.billing";
        CreateContext().TrySetExplicitTarget(null, null);
    }

    [Theory]
    [InlineData("@acme.billing.preview.sbt.contoso")]
    [InlineData("not a key")]
    public void User_NeverReadsStorytellerKey(string key)
    {
        _environment[StorytellerVariables.KEY] = key;
        var context = CreateContext();

        context.TrySetExplicitTarget(null, null);

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("house", "stored", "staging"));
    }

    [Fact]
    public async Task FederatedMachine_IsBoundToTheProjectOfStorytellerKey()
    {
        _environment[StorytellerVariables.KEY] = Key;
        UseFederated();
        _http.Respond(HttpStatusCode.OK, """{"access_token":"st-billing","token_type":"Bearer","expires_in":900}""");
        var context = CreateContext();

        context.TrySetExplicitTarget(null, null);

        (await _machine!.GetAccessTokenAsync()).ShouldBe("st-billing");
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://storyteller.example.com/api/v1/acme/billing/access/token"));
    }

    // One key, one project: sform's target, the unbound SDK credential of a provider (StorytellerCredential.FromEnvironment
    // exchanges at the project of STORYTELLER_KEY) and the provider's own parse (FullKey.Parse) agree.
    [Fact]
    public async Task StorytellerKey_NamesTheSameProject_ForSformTheSdkAndTheProvider()
    {
        const string key = "@Globex.Shipping.release.cnt.fabrikam.eu";
        const string tokenEndpoint = "https://storyteller.example.com/api/v1/Globex/Shipping/access/token";
        _environment[StorytellerVariables.KEY] = key;
        UseFederated();
        _http.Respond(HttpStatusCode.OK, """{"access_token":"st-sform","token_type":"Bearer","expires_in":900}""")
            .Respond(HttpStatusCode.OK, """{"access_token":"st-sdk","token_type":"Bearer","expires_in":900}""");
        var context = CreateContext();
        context.TrySetExplicitTarget(null, null);
        await _machine!.GetAccessTokenAsync();

        var sdk = StorytellerCredential.FromEnvironment(name => _environment.GetValueOrDefault(name), _fileSystem)!.WithPrimaryHandler(_http);
        var authorization = await sdk.GetAuthorizationAsync(null, CancellationToken.None);
        var provider = FullKey.Parse(key);

        (context.OrganizationName, context.ProjectName, context.ViewName).ShouldBe(("Globex", "Shipping", "release"));
        (provider.OrganizationName, provider.ProjectName, provider.ViewName).ShouldBe((context.OrganizationName, context.ProjectName, context.ViewName));
        authorization!.Parameter.ShouldBe("st-sdk");
        _http.Requests.Select(request => request.Uri.AbsoluteUri).ShouldBe([tokenEndpoint, tokenEndpoint]);
    }

    private void SetIfGiven(string variable, string? value)
    {
        if (value is not null)
        {
            _environment[variable] = value;
        }
    }

    private void UseFederated()
    {
        var path = MockUnixSupport.Path(@"C:\ci\platform-token");
        _fileSystem.AddFile(path, new MockFileData("platform-token"));
        _environment[StorytellerVariables.CLIENT_ID] = FederatedMachineId;
        _environment[StorytellerVariables.TOKEN_FILE] = path;
    }

    private void UseClientCredentials()
    {
        _environment[StorytellerVariables.CLIENT_ID] = "client_01";
        _environment[StorytellerVariables.CLIENT_SECRET] = "secret";
        _environment[StorytellerVariables.TOKEN_URL] = "https://auth.example.com/oauth2/token";
    }

    private CommandContext CreateContext(AccessDefaultOptions? file = null)
    {
        _machine = TestMachines.Create(_environment, _fileSystem, _http);
        return new CommandContext(Options.Create(file ?? _file), _machine);
    }
}
