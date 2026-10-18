using System.Collections.Generic;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class MachineSignInSettingsTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string MachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";

    private const string ClientIdWithoutSourceMessage =
        "STORYTELLER_CLIENT_ID needs STORYTELLER_CLIENT_SECRET for client credentials, or a platform token for a federated machine: STORYTELLER_SUBJECT_TOKEN_FILE, or a GitHub Actions job with permissions: id-token: write.";

    [Fact]
    public void FromEnvironment_TrimsValuesAndTreatsEmptyAsUnset()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.API_URL] = $"  {ApiUrl} ",
            [StorytellerVariables.API_KEY] = "   ",
            [StorytellerVariables.CLIENT_ID] = string.Empty,
            [StorytellerVariables.PROJECT] = " acme.billing\n",
        });

        settings.ApiUrl.ShouldBe(ApiUrl);
        settings.Project.ShouldBe("acme.billing");
        settings.ApiKey.ShouldBeNull();
        settings.ClientId.ShouldBeNull();
        settings.IsConfigured.ShouldBeFalse();
        settings.CredentialVariables.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ApiKey 2s.abc.def")]
    [InlineData("apikey   2s.abc.def")]
    [InlineData("2s.abc.def")]
    public void FromEnvironment_RemovesTheApiKeyScheme(string value)
    {
        Read(new() { [StorytellerVariables.API_KEY] = value }).ApiKey.ShouldBe("2s.abc.def");
    }

    [Theory]
    [InlineData(MachineSignInKind.ApiKey, StorytellerVariables.API_KEY)]
    [InlineData(MachineSignInKind.Certificate, StorytellerVariables.CERTIFICATE_FILE)]
    [InlineData(MachineSignInKind.Certificate, StorytellerVariables.CERTIFICATE_FILE, StorytellerVariables.CERTIFICATE_PASSWORD)]
    [InlineData(MachineSignInKind.CertificateAndApiKey, StorytellerVariables.API_KEY, StorytellerVariables.CERTIFICATE_FILE, StorytellerVariables.CERTIFICATE_PASSWORD)]
    [InlineData(MachineSignInKind.ClientCredentials, StorytellerVariables.CLIENT_ID, StorytellerVariables.CLIENT_SECRET)]
    [InlineData(MachineSignInKind.ClientCredentials, StorytellerVariables.CLIENT_ID, StorytellerVariables.CLIENT_SECRET, StorytellerVariables.TOKEN_URL, StorytellerVariables.TOKEN_SCOPE)]
    [InlineData(MachineSignInKind.TokenFile, StorytellerVariables.TOKEN_FILE)]
    [InlineData(MachineSignInKind.Federated, StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE)]
    [InlineData(MachineSignInKind.Federated, StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.SUBJECT_TOKEN_TYPE, StorytellerVariables.TOKEN_SCOPE)]
    public void GetKind_FollowsTheVariablesThatAreSet(MachineSignInKind expected, params string[] variables)
    {
        var settings = Machine(variables);

        settings.IsConfigured.ShouldBeTrue();
        settings.CredentialVariables.ShouldBe(variables, ignoreOrder: true);
        settings.GetKind().ShouldBe(expected);
    }

    [Fact]
    public void GetKind_VariablesOfDifferentKinds_FailWithTheirNames()
    {
        var exception = Should.Throw<AuthenticationException>(() => Machine(StorytellerVariables.API_KEY, StorytellerVariables.TOKEN_FILE).GetKind());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("Machine sign-in variables of different kinds are set: STORYTELLER_API_KEY, STORYTELLER_TOKEN_FILE. Set the variables of one kind only.");
    }

    [Theory]
    [InlineData(StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.CLIENT_SECRET)]
    [InlineData(StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.TOKEN_URL)]
    [InlineData(StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.API_KEY)]
    [InlineData(StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.TOKEN_FILE)]
    [InlineData(StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_TYPE, StorytellerVariables.CLIENT_SECRET)]
    [InlineData(StorytellerVariables.API_KEY, StorytellerVariables.CLIENT_ID)]
    public void GetKind_FederationOrSharedVariablesWithAnotherKind_FailAsMixedKinds(params string[] variables)
    {
        var settings = Machine(variables);

        var exception = Should.Throw<AuthenticationException>(() => settings.GetKind());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"Machine sign-in variables of different kinds are set: {string.Join(", ", settings.CredentialVariables)}. Set the variables of one kind only.");
    }

    [Theory]
    [InlineData(ClientIdWithoutSourceMessage, StorytellerVariables.CLIENT_ID)]
    [InlineData(ClientIdWithoutSourceMessage, StorytellerVariables.CLIENT_ID, StorytellerVariables.TOKEN_SCOPE)]
    [InlineData("Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_SECRET is missing.", StorytellerVariables.CLIENT_ID, StorytellerVariables.TOKEN_URL)]
    [InlineData("Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_ID is missing.", StorytellerVariables.CLIENT_SECRET, StorytellerVariables.TOKEN_URL)]
    [InlineData("Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET are missing.", StorytellerVariables.TOKEN_SCOPE)]
    [InlineData("A federated machine needs STORYTELLER_CLIENT_ID, the machine id.", StorytellerVariables.SUBJECT_TOKEN_FILE)]
    [InlineData("A federated machine needs STORYTELLER_CLIENT_ID, the machine id.", StorytellerVariables.SUBJECT_TOKEN_TYPE, StorytellerVariables.TOKEN_SCOPE)]
    [InlineData("A federated machine needs a platform token: set STORYTELLER_SUBJECT_TOKEN_FILE, or run in a GitHub Actions job with permissions: id-token: write.", StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_TYPE)]
    [InlineData("STORYTELLER_CERTIFICATE_PASSWORD is set without STORYTELLER_CERTIFICATE_FILE.", StorytellerVariables.CERTIFICATE_PASSWORD)]
    [InlineData("STORYTELLER_CERTIFICATE_PASSWORD is set without STORYTELLER_CERTIFICATE_FILE.", StorytellerVariables.API_KEY, StorytellerVariables.CERTIFICATE_PASSWORD)]
    public void GetKind_IncompleteCredential_SaysWhatIsMissing(string message, params string[] variables)
    {
        var exception = Should.Throw<AuthenticationException>(() => Machine(variables).GetKind());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe(message);
    }

    [Fact]
    public void GetKind_WithoutApiUrl_AsksForIt()
    {
        var exception = Should.Throw<AuthenticationException>(() => Read(new() { [StorytellerVariables.API_KEY] = "2s.a.b" }).GetKind());

        exception.Message.ShouldBe("Set STORYTELLER_API_URL to the API base URL, for example https://<host>/api.");
    }

    [Theory]
    [InlineData("http://storyteller.example.com/api", false)]
    [InlineData("ftp://storyteller.example.com/api", false)]
    [InlineData("storyteller.example.com/api", false)]
    [InlineData("/api", false)]
    [InlineData("https://storyteller.example.com/api", true)]
    [InlineData("http://localhost:7071/api", true)]
    [InlineData("http://127.0.0.1:7071/api", true)]
    [InlineData("http://[::1]:7071/api", true)]
    [InlineData("http://LOCALHOST:7071/api", true)]
    public void GetKind_ApiUrl_MustBeHttpsOrLocal(string apiUrl, bool isValid)
    {
        var settings = Read(new() { [StorytellerVariables.API_URL] = apiUrl, [StorytellerVariables.API_KEY] = "2s.a.b" });

        if (isValid)
        {
            settings.GetKind().ShouldBe(MachineSignInKind.ApiKey);
            return;
        }

        Should.Throw<AuthenticationException>(() => settings.GetKind())
            .Message.ShouldBe("STORYTELLER_API_URL must be an absolute https URL; http is allowed only for localhost.");
    }

    [Fact]
    public void GetKind_TokenUrlOverHttp_IsRefused()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.API_URL] = ApiUrl,
            [StorytellerVariables.CLIENT_ID] = "client_01",
            [StorytellerVariables.CLIENT_SECRET] = "secret",
            [StorytellerVariables.TOKEN_URL] = "http://auth.example.com/oauth2/token",
        });

        Should.Throw<AuthenticationException>(() => settings.GetKind())
            .Message.ShouldBe("STORYTELLER_TOKEN_URL must be an absolute https URL; http is allowed only for localhost.");
    }

    [Theory]
    [InlineData("acme")]
    [InlineData(".billing")]
    [InlineData("acme.")]
    public void GetKind_MalformedProject_IsRefused(string project)
    {
        var settings = Read(new() { [StorytellerVariables.API_URL] = ApiUrl, [StorytellerVariables.API_KEY] = "2s.a.b", [StorytellerVariables.PROJECT] = project });

        Should.Throw<AuthenticationException>(() => settings.GetKind())
            .Message.ShouldBe("STORYTELLER_PROJECT must be <organization>.<project>.");
    }

    [Fact]
    public void FromEnvironment_ReadsTheFederationVariables()
    {
        var settings = Read(new()
        {
            [StorytellerVariables.SUBJECT_TOKEN_FILE] = " /var/run/secrets/storyteller/token\n",
            [StorytellerVariables.SUBJECT_TOKEN_TYPE] = " id_token ",
            [GitHubActionsVariables.ID_TOKEN_REQUEST_URL] = $"  {GitHubRequestUrl} ",
            [GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN] = "   ",
        });

        settings.SubjectTokenFile.ShouldBe("/var/run/secrets/storyteller/token");
        settings.SubjectTokenType.ShouldBe("id_token");
        settings.GitHubTokenRequestUrl.ShouldBe(GitHubRequestUrl);
        settings.GitHubTokenRequestToken.ShouldBeNull();
        settings.HasGitHubTokenSource.ShouldBeFalse();
        settings.CredentialVariables.ShouldBe([StorytellerVariables.SUBJECT_TOKEN_FILE, StorytellerVariables.SUBJECT_TOKEN_TYPE]);
    }

    [Fact]
    public void GetKind_ClientIdAndASubjectFile_IsFederatedWithTheFile()
    {
        // No GUID needed: the file is an explicit choice.
        var settings = Read(new()
        {
            [StorytellerVariables.API_URL] = ApiUrl,
            [StorytellerVariables.CLIENT_ID] = "machine-01",
            [StorytellerVariables.SUBJECT_TOKEN_FILE] = "/var/run/secrets/storyteller/token",
        });

        settings.GetKind().ShouldBe(MachineSignInKind.Federated);
        settings.GetFederatedTokenSource().ShouldBe(FederatedTokenSource.File);
    }

    [Fact]
    public void GetKind_AMachineIdInAGitHubActionsJob_IsFederatedWithGitHub()
    {
        var settings = Read(InGitHubActionsJob(new() { [StorytellerVariables.CLIENT_ID] = MachineId }));

        settings.IsConfigured.ShouldBeTrue();
        settings.GetKind().ShouldBe(MachineSignInKind.Federated);
        settings.GetFederatedTokenSource().ShouldBe(FederatedTokenSource.GitHubActions);
        settings.CredentialVariables.ShouldBe([StorytellerVariables.CLIENT_ID]);
    }

    [Fact]
    public void GetKind_ASubjectFileInAGitHubActionsJob_WinsOverGitHub()
    {
        var settings = Read(InGitHubActionsJob(new()
        {
            [StorytellerVariables.CLIENT_ID] = MachineId,
            [StorytellerVariables.SUBJECT_TOKEN_FILE] = "/tmp/platform-token",
        }));

        settings.GetKind().ShouldBe(MachineSignInKind.Federated);
        settings.GetFederatedTokenSource().ShouldBe(FederatedTokenSource.File);
    }

    [Fact]
    public void GetKind_GitHubActionsVariablesAlone_ConfigureNoMachine()
    {
        var settings = Read(InGitHubActionsJob(new()));

        settings.HasGitHubTokenSource.ShouldBeTrue();
        settings.IsConfigured.ShouldBeFalse();
        settings.CredentialVariables.ShouldBeEmpty();
        Should.Throw<AuthenticationException>(() => settings.GetKind()).Message.ShouldBe("No machine sign-in variable is set.");
    }

    // A client-credentials job whose secret is missing has an empty STORYTELLER_CLIENT_SECRET; it must not turn into an exchange.
    [Theory]
    [InlineData("client_01HXYZ", true)]
    [InlineData("42.sform.acme.billing.0a1b", true)]
    [InlineData(MachineId, false)]
    public void GetKind_AClientIdWithoutASecret_IsFederatedOnlyForAMachineIdInAGitHubActionsJob(string clientId, bool isGitHubComplete)
    {
        var environment = InGitHubActionsJob(new()
        {
            [StorytellerVariables.CLIENT_ID] = clientId,
            [StorytellerVariables.CLIENT_SECRET] = string.Empty,
        });

        if (!isGitHubComplete)
        {
            environment.Remove(GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN);
        }

        var exception = Should.Throw<AuthenticationException>(() => Read(environment).GetKind());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe(ClientIdWithoutSourceMessage);
    }

    [Fact]
    public void GetKind_ClientCredentialsInAGitHubActionsJob_StayClientCredentials()
    {
        // An Entra ID appId is a GUID too.
        var settings = Read(InGitHubActionsJob(new()
        {
            [StorytellerVariables.CLIENT_ID] = MachineId,
            [StorytellerVariables.CLIENT_SECRET] = "secret",
        }));

        settings.GetKind().ShouldBe(MachineSignInKind.ClientCredentials);
        settings.CredentialVariables.ShouldBe([StorytellerVariables.CLIENT_ID, StorytellerVariables.CLIENT_SECRET]);
    }

    [Fact]
    public void GetKind_AnApiKeyInAGitHubActionsJob_StaysAnApiKey()
    {
        var settings = Read(InGitHubActionsJob(new() { [StorytellerVariables.API_KEY] = "2s.a.b" }));

        settings.GetKind().ShouldBe(MachineSignInKind.ApiKey);
        settings.CredentialVariables.ShouldBe([StorytellerVariables.API_KEY]);
    }

    [Theory]
    [InlineData("http://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0", false)]
    [InlineData("/abc/idtoken", false)]
    [InlineData("https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0", true)]
    [InlineData("http://localhost:8080/idtoken", true)]
    public void GetKind_GitHubTokenRequestUrl_MustBeHttpsOrLocal(string requestUrl, bool isValid)
    {
        var environment = InGitHubActionsJob(new() { [StorytellerVariables.CLIENT_ID] = MachineId });
        environment[GitHubActionsVariables.ID_TOKEN_REQUEST_URL] = requestUrl;
        var settings = Read(environment);

        if (isValid)
        {
            settings.GetKind().ShouldBe(MachineSignInKind.Federated);
            return;
        }

        var exception = Should.Throw<AuthenticationException>(() => settings.GetKind());
        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("ACTIONS_ID_TOKEN_REQUEST_URL must be an absolute https URL; http is allowed only for localhost.");
    }

    [Theory]
    [InlineData(null, TokenExchange.JwtTokenType)]
    [InlineData("jwt", TokenExchange.JwtTokenType)]
    [InlineData("JWT", TokenExchange.JwtTokenType)]
    [InlineData("id_token", TokenExchange.IdTokenType)]
    [InlineData("Access_Token", TokenExchange.AccessTokenType)]
    [InlineData("urn:ietf:params:oauth:token-type:jwt", TokenExchange.JwtTokenType)]
    [InlineData("urn:ietf:params:oauth:token-type:id_token", TokenExchange.IdTokenType)]
    [InlineData("urn:ietf:params:oauth:token-type:access_token", TokenExchange.AccessTokenType)]
    public void NormalizeSubjectTokenType_AcceptsTheShortNamesAndTheUrns(string? value, string expected)
    {
        MachineSignInSettings.NormalizeSubjectTokenType(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("saml2")]
    [InlineData("urn:ietf:params:oauth:token-type:saml2")]
    [InlineData("token-type:jwt")]
    public void GetKind_UnknownSubjectTokenType_IsRefused(string value)
    {
        var settings = Read(new()
        {
            [StorytellerVariables.API_URL] = ApiUrl,
            [StorytellerVariables.CLIENT_ID] = MachineId,
            [StorytellerVariables.SUBJECT_TOKEN_FILE] = "/tmp/platform-token",
            [StorytellerVariables.SUBJECT_TOKEN_TYPE] = value,
        });

        MachineSignInSettings.NormalizeSubjectTokenType(value).ShouldBeNull();
        Should.Throw<AuthenticationException>(() => settings.GetKind())
            .Message.ShouldBe("STORYTELLER_SUBJECT_TOKEN_TYPE must be jwt, id_token or access_token, or the full urn:ietf:params:oauth:token-type: URN.");
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
            [StorytellerVariables.CERTIFICATE_PASSWORD] = "top-secret-password",
            [StorytellerVariables.CLIENT_SECRET] = "top-secret-client",
            [GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN] = "top-secret-request-token",
        });

        settings.ToString()!.ShouldNotContain("top-secret");
    }

    private static MachineSignInSettings Machine(params string[] variables)
    {
        var environment = new Dictionary<string, string> { [StorytellerVariables.API_URL] = ApiUrl };

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

    // The variables the runner sets in a job with permissions: id-token: write.
    private static Dictionary<string, string> InGitHubActionsJob(Dictionary<string, string> environment)
    {
        environment[StorytellerVariables.API_URL] = ApiUrl;
        environment[GitHubActionsVariables.ID_TOKEN_REQUEST_URL] = GitHubRequestUrl;
        environment[GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN] = "request-token";
        return environment;
    }

    private static MachineSignInSettings Read(Dictionary<string, string> environment)
    {
        return MachineSignInSettings.FromEnvironment(name => environment.GetValueOrDefault(name));
    }
}
