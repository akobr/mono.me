using System.Collections.Generic;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class MachineSignInSettingsTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";

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
    [InlineData("Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_SECRET is missing.", StorytellerVariables.CLIENT_ID)]
    [InlineData("Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_ID is missing.", StorytellerVariables.CLIENT_SECRET, StorytellerVariables.TOKEN_URL)]
    [InlineData("Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET are missing.", StorytellerVariables.TOKEN_SCOPE)]
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
        });

        settings.ToString()!.ShouldNotContain("top-secret");
    }

    private static MachineSignInSettings Machine(params string[] variables)
    {
        var environment = new Dictionary<string, string> { [StorytellerVariables.API_URL] = ApiUrl };

        foreach (var variable in variables)
        {
            environment[variable] = variable == StorytellerVariables.TOKEN_URL ? "https://auth.example.com/oauth2/token" : $"value-of-{variable}";
        }

        return Read(environment);
    }

    private static MachineSignInSettings Read(Dictionary<string, string> environment)
    {
        return MachineSignInSettings.FromEnvironment(name => environment.GetValueOrDefault(name));
    }
}
