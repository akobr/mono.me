using System.Collections.Generic;
using _42.Platform.Cli.Authentication;
using _42.Platform.Storyteller.Sdk.Credentials;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

// The settings on the shared credential contract of the SDK. The rules of the credential variables themselves are
// tested once, in Sdk.NSwag/test/Credentials (StorytellerCredentialVariablesTests, FromEnvironmentTests).
public class MachineSignInSettingsTests
{
    private const string Endpoint = "https://storyteller.example.com/api";
    private const string MachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";

    [Fact]
    public void FromEnvironment_TrimsValuesAndTreatsEmptyAsUnset()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.ENDPOINT] = $"  {Endpoint} ",
            [StorytellerVariables.API_KEY] = "   ",
            [StorytellerVariables.CLIENT_ID] = string.Empty,
            [TargetVariables.PROJECT] = " acme.billing\n",
            [TargetVariables.VIEW] = " preview ",
        });

        settings.Endpoint.ShouldBe(Endpoint);
        settings.EndpointVariable.ShouldBe(StorytellerVariables.ENDPOINT);
        settings.Project.ShouldBe("acme.billing");
        settings.View.ShouldBe("preview");
        settings.IsConfigured.ShouldBeFalse();
        settings.CredentialVariables.ShouldBeEmpty();
        settings.DeprecatedVariables.ShouldBeEmpty();
        Should.Throw<AuthenticationException>(() => settings.GetKind()).Message.ShouldBe("No machine sign-in variable is set.");
    }

    // One to one with the kinds of the SDK; the reading itself is the SDK's.
    [Theory]
    [InlineData(MachineSignInKind.ApiKey, StorytellerVariables.API_KEY)]
    [InlineData(MachineSignInKind.Certificate, StorytellerVariables.CLIENT_CERTIFICATE)]
    [InlineData(MachineSignInKind.Certificate, StorytellerVariables.CERTIFICATE_FILE, StorytellerVariables.CERTIFICATE_PASSWORD)]
    [InlineData(MachineSignInKind.CertificateAndApiKey, StorytellerVariables.API_KEY, StorytellerVariables.CLIENT_CERTIFICATE, StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD)]
    [InlineData(MachineSignInKind.ClientCredentials, StorytellerVariables.CLIENT_ID, StorytellerVariables.CLIENT_SECRET, StorytellerVariables.TOKEN_URL, StorytellerVariables.TOKEN_SCOPE)]
    [InlineData(MachineSignInKind.TokenFile, StorytellerVariables.TOKEN_FILE)]
    [InlineData(MachineSignInKind.Federated, StorytellerVariables.CLIENT_ID, StorytellerVariables.TOKEN_FILE)]
    [InlineData(MachineSignInKind.Federated, StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.SUBJECT_TOKEN_TYPE, StorytellerVariables.TOKEN_SCOPE)]
    public void GetKind_FollowsTheKindOfTheSdk(MachineSignInKind expected, params string[] variables)
    {
        var settings = Machine(variables);

        settings.IsConfigured.ShouldBeTrue();
        settings.CredentialVariables.ShouldBe(variables, ignoreOrder: true);
        settings.GetKind().ShouldBe(expected);
        settings.GetCredential().Names.ShouldBe(settings.CredentialVariables);
    }

    // Change against the sform names: a token file with a client id was a mixed-kinds error (exit 2).
    [Fact]
    public void GetKind_ATokenFileWithAClientId_IsFederated()
    {
        var settings = Machine(StorytellerVariables.CLIENT_ID, StorytellerVariables.TOKEN_FILE);

        settings.GetKind().ShouldBe(MachineSignInKind.Federated);
        settings.GetCredential().SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.File);
    }

    // Change against the sform names: the scheme alone was sent as the key.
    [Fact]
    public void GetKind_TheApiKeySchemeAlone_IsRefused()
    {
        var settings = Read(new() { [StorytellerVariables.ENDPOINT] = Endpoint, [StorytellerVariables.API_KEY] = "ApiKey " });

        settings.IsConfigured.ShouldBeTrue();
        var exception = Should.Throw<AuthenticationException>(() => settings.GetKind());
        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("STORYTELLER_API_KEY holds no key.");
    }

    // The names stay known, for the messages of every command, while the credential reports why it is invalid.
    [Fact]
    public void InvalidVariables_StayConfigured_AndFailEveryCallWithANewException()
    {
        var settings = Machine(StorytellerVariables.API_KEY, StorytellerVariables.TOKEN_FILE);

        settings.IsConfigured.ShouldBeTrue();
        settings.CredentialVariables.ShouldBe([StorytellerVariables.API_KEY, StorytellerVariables.TOKEN_FILE]);
        var first = Should.Throw<AuthenticationException>(() => settings.GetKind());
        var second = Should.Throw<AuthenticationException>(() => settings.GetCredential());

        first.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        first.Message.ShouldBe("Storyteller credential variables of different kinds are set: STORYTELLER_API_KEY, STORYTELLER_TOKEN_FILE. Set the variables of one kind only.");
        second.Message.ShouldBe(first.Message);
        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void GetKind_GitHubActionsVariablesAlone_ConfigureNoMachine()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.ENDPOINT] = Endpoint,
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL] = GitHubRequestUrl,
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN] = "request-token",
        });

        settings.IsConfigured.ShouldBeFalse();
        settings.CredentialVariables.ShouldBeEmpty();
        Should.Throw<AuthenticationException>(() => settings.GetKind()).Message.ShouldBe("No machine sign-in variable is set.");
    }

    [Fact]
    public void GetKind_AMachineIdInAGitHubActionsJob_IsFederatedWithoutTheGitHubNames()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.ENDPOINT] = Endpoint,
            [StorytellerVariables.CLIENT_ID] = MachineId,
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL] = GitHubRequestUrl,
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN] = "request-token",
        });

        settings.GetKind().ShouldBe(MachineSignInKind.Federated);
        settings.GetCredential().SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.GitHubActions);
        settings.CredentialVariables.ShouldBe([StorytellerVariables.CLIENT_ID]);
    }

    [Fact]
    public void GetKind_WithoutAnEndpoint_AsksForTheCanonicalName()
    {
        var exception = Should.Throw<AuthenticationException>(() => Read(new() { [StorytellerVariables.API_KEY] = "2s.a.b" }).GetKind());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("Set STORYTELLER_ENDPOINT to the API base URL, for example https://<host>/api.");
    }

    [Fact]
    public void Endpoint_FallsBackToTheDeprecatedName()
    {
        var settings = Read(new() { [StorytellerVariables.API_URL] = Endpoint, [StorytellerVariables.API_KEY] = "2s.a.b" });

        settings.Endpoint.ShouldBe(Endpoint);
        settings.EndpointVariable.ShouldBe(StorytellerVariables.API_URL);
        settings.GetEndpoint().ShouldBe(new System.Uri(Endpoint));
        settings.GetKind().ShouldBe(MachineSignInKind.ApiKey);
        settings.DeprecatedVariables.ShouldBe([StorytellerVariables.API_URL]);
    }

    [Theory]
    [InlineData(Endpoint, true)]
    [InlineData("https://storyteller.example.com/other", false)]
    public void Endpoint_UnderBothNames_MustAgree(string deprecatedValue, bool agrees)
    {
        var settings = Read(new()
        {
            [StorytellerVariables.ENDPOINT] = Endpoint,
            [StorytellerVariables.API_URL] = deprecatedValue,
            [StorytellerVariables.API_KEY] = "2s.a.b",
        });

        if (agrees)
        {
            settings.GetKind().ShouldBe(MachineSignInKind.ApiKey);
            settings.EndpointVariable.ShouldBe(StorytellerVariables.ENDPOINT);

            // Reported whenever it is set, as the SDK reports a deprecated credential name next to its canonical one.
            settings.DeprecatedVariables.ShouldBe([StorytellerVariables.API_URL]);
            return;
        }

        // Reading never throws: Startup builds the settings before any command runs.
        settings.Endpoint.ShouldBeNull();
        var exception = Should.Throw<AuthenticationException>(() => settings.GetKind());
        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("STORYTELLER_ENDPOINT and STORYTELLER_API_URL are both set, to different values; set one of them.");
        exception.Message.ShouldNotContain("example.com");
    }

    // Change against the sform rule: user info, and http to the name "loopback", are refused too.
    [Theory]
    [InlineData("https://storyteller.example.com/api", null)]
    [InlineData("http://localhost:7071/api", null)]
    [InlineData("http://LOCALHOST:7071/api", null)]
    [InlineData("http://127.0.0.1:7071/api", null)]
    [InlineData("http://[::1]:7071/api", null)]
    [InlineData("http://storyteller.example.com/api", "{0} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.")]
    [InlineData("http://loopback:7071/api", "{0} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.")]
    [InlineData("https://user:p4ssw0rd@storyteller.example.com/api", "{0} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.")]
    [InlineData("ftp://storyteller.example.com/api", "{0} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.")]
    [InlineData("/api", "{0} must be an absolute https URL without user info; http is allowed only for localhost or a loopback address.")]
    [InlineData("https://storyteller.example.com/api?tenant=acme", "{0} must not have a query or a fragment; set the API base, /api included.")]
    public void Endpoint_FollowsTheUrlRuleOfTheSdk_AndNamesTheVariableAsSet(string endpoint, string? message)
    {
        foreach (var variable in new[] { StorytellerVariables.ENDPOINT, StorytellerVariables.API_URL })
        {
            var settings = Read(new() { [variable] = endpoint, [StorytellerVariables.API_KEY] = "2s.a.b" });

            if (message is null)
            {
                settings.GetKind().ShouldBe(MachineSignInKind.ApiKey);
                continue;
            }

            var exception = Should.Throw<AuthenticationException>(() => settings.GetKind());
            exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
            exception.Message.ShouldBe(string.Format(message, variable));
            exception.Message.ShouldNotContain("p4ssw0rd");
        }
    }

    [Theory]
    [InlineData("acme")]
    [InlineData(".billing")]
    [InlineData("acme.")]
    public void GetKind_MalformedProject_IsRefused(string project)
    {
        var settings = Read(new() { [StorytellerVariables.ENDPOINT] = Endpoint, [StorytellerVariables.API_KEY] = "2s.a.b", [TargetVariables.PROJECT] = project });

        Should.Throw<AuthenticationException>(() => settings.GetKind())
            .Message.ShouldBe("STORYTELLER_PROJECT must be <organization>.<project>.");
    }

    [Fact]
    public void DeprecatedVariables_NameEveryDeprecatedNameInUse()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.API_URL] = Endpoint,
            [StorytellerVariables.API_KEY] = "2s.a.b",
            [StorytellerVariables.CERTIFICATE_FILE] = "/ci/machine.pfx",
            [StorytellerVariables.CERTIFICATE_PASSWORD] = "pfx-password",
        });

        settings.GetKind().ShouldBe(MachineSignInKind.CertificateAndApiKey);
        settings.DeprecatedVariables.ShouldBe([StorytellerVariables.API_URL, StorytellerVariables.CERTIFICATE_FILE, StorytellerVariables.CERTIFICATE_PASSWORD]);
    }

    [Fact]
    public void DeprecatedVariables_AreKnownEvenWhenTheCredentialIsInvalid()
    {
        var settings = Machine(StorytellerVariables.SUBJECT_TOKEN_FILE);

        Should.Throw<AuthenticationException>(() => settings.GetKind()).Message.ShouldBe("A federated machine needs STORYTELLER_CLIENT_ID, the machine id.");
        settings.DeprecatedVariables.ShouldBe([StorytellerVariables.SUBJECT_TOKEN_FILE]);
    }

    [Theory]
    [InlineData("acme.billing", "acme", "billing")]
    [InlineData("acme.billing.eu", "acme", "billing.eu")]
    public void TrySplitProject_SplitsAtTheFirstDot(string projectKey, string organization, string project)
    {
        MachineSignInSettings.TrySplitProject(projectKey, out var actualOrganization, out var actualProject).ShouldBeTrue();

        actualOrganization.ShouldBe(organization);
        actualProject.ShouldBe(project);
    }

    [Fact]
    public void ToString_PrintsNoSecret()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.API_KEY] = "2s.key.top-secret-key",
            [StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD] = "top-secret-password",
            [StorytellerVariables.CLIENT_SECRET] = "top-secret-client",
            [StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN] = "top-secret-request-token",
        });

        // The class has no ToString of its own; this guards against one that would print the values.
        settings.ToString()!.ShouldNotContain("top-secret");
        Should.Throw<AuthenticationException>(() => settings.GetKind()).Message.ShouldNotContain("top-secret");

        var clientCredentials = Read(new()
        {
            [StorytellerVariables.ENDPOINT] = Endpoint,
            [StorytellerVariables.CLIENT_ID] = "client_01",
            [StorytellerVariables.CLIENT_SECRET] = "top-secret-client",
        });

        clientCredentials.GetKind().ShouldBe(MachineSignInKind.ClientCredentials);
        clientCredentials.GetCredential().ToString().ShouldNotContain("top-secret");
        clientCredentials.ToString()!.ShouldNotContain("top-secret");
    }

    private static MachineSignInSettings Machine(params string[] variables)
    {
        var environment = new Dictionary<string, string> { [StorytellerVariables.ENDPOINT] = Endpoint };

        foreach (var variable in variables)
        {
            environment[variable] = variable switch
            {
                StorytellerVariables.TOKEN_URL => "https://auth.example.com/oauth2/token",
                StorytellerVariables.SUBJECT_TOKEN_TYPE => "jwt",
                _ => $"value-of-{variable}",
            };
        }

        return Read(environment);
    }

    private static MachineSignInSettings Read(Dictionary<string, string> environment)
    {
        return MachineSignInSettings.FromEnvironment(name => environment.GetValueOrDefault(name));
    }
}
