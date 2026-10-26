using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

// The rules of the shared credential contract, one place for the SDK, the provider and sform.
public class StorytellerCredentialVariablesTests
{
    private const string ApiKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ.k3y-s3cret";
    private const string ClientSecret = "cl13nt-s3cret";
    private const string MachineId = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string GitHubRequestToken = "github-request-t0ken";

    private readonly Dictionary<string, string?> _variables = [];
    private readonly MockFileSystem _fileSystem = new();

    public StorytellerCredentialVariablesTests()
    {
        _fileSystem.AddFile("/run/machine.pfx", new MockFileData(TestCertificates.Pkcs12()));
        _fileSystem.AddFile("/run/token", new MockFileData("token"));
    }

    [Fact]
    public void NoCredentialVariable_IsNull()
    {
        _variables["STORYTELLER_ENDPOINT"] = "https://api.example.com/api";
        _variables["STORYTELLER_KEY"] = "@acme.billing.default.sbt.northwind";

        Read().ShouldBeNull();
    }

    // Both are in every step of a job with id-token: write; alone they configure no machine.
    [Fact]
    public void TheGitHubActionsVariablesAlone_AreNoCredential()
    {
        UseGitHubActions();

        Read().ShouldBeNull();
        StorytellerCredential.FromEnvironment(name => _variables.GetValueOrDefault(name), _fileSystem).ShouldBeNull();
    }

    [Fact]
    public void TheTargetVariables_AreNotRead()
    {
        _variables["STORYTELLER_ENDPOINT"] = "https://one.example.com/api";
        _variables["STORYTELLER_API_URL"] = "https://two.example.com/api";
        _variables["STORYTELLER_KEY"] = "not a key";
        _variables["STORYTELLER_API_KEY"] = ApiKey;

        Read().ShouldNotBeNull().Kind.ShouldBe(StorytellerCredentialKind.ApiKey);
    }

    [Theory]
    [InlineData("ApiKey " + ApiKey)]
    [InlineData("apikey\t" + ApiKey)]
    [InlineData("  " + ApiKey + "  ")]
    public void AnApiKey_IsReadWithoutItsScheme(string value)
    {
        _variables["STORYTELLER_API_KEY"] = value;

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.ApiKey);
        variables.ApiKeyMachine.ShouldBe("YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ");
        PublicValues(variables).ShouldNotContain(value => value.Contains("s3cret"));
        variables.Names.ShouldBe(["STORYTELLER_API_KEY"]);
        variables.DeprecatedNames.ShouldBeEmpty();
        variables.SubjectTokenSource.ShouldBeNull();
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("APIKEY   ")]
    public void TheApiKeySchemeAlone_HoldsNoKey(string value)
    {
        _variables["STORYTELLER_API_KEY"] = value;

        ShouldFail("STORYTELLER_API_KEY").Message.ShouldBe("STORYTELLER_API_KEY holds no key.");
    }

    [Fact]
    public void ACertificate_UnderTheDeprecatedNames_NamesThem()
    {
        _variables["STORYTELLER_CERTIFICATE_FILE"] = "/run/machine.pfx";
        _variables["STORYTELLER_CERTIFICATE_PASSWORD"] = TestCertificates.Password;

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.Certificate);
        variables.Names.ShouldBe(["STORYTELLER_CERTIFICATE_FILE", "STORYTELLER_CERTIFICATE_PASSWORD"]);
        variables.DeprecatedNames.ShouldBe(["STORYTELLER_CERTIFICATE_FILE", "STORYTELLER_CERTIFICATE_PASSWORD"]);
        variables.ApiKeyMachine.ShouldBeNull();
        variables.CreateCredential(_fileSystem).ShouldBeOfType<CertificateCredential>().ClientCertificate.ShouldNotBeNull();
    }

    [Fact]
    public void ACertificateWithAnApiKey()
    {
        _variables["STORYTELLER_API_KEY"] = ApiKey;
        _variables["STORYTELLER_CLIENT_CERTIFICATE"] = "/run/machine.pfx";
        _variables["STORYTELLER_CLIENT_CERTIFICATE_PASSWORD"] = TestCertificates.Password;

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.CertificateAndApiKey);
        variables.ApiKeyMachine.ShouldBe("YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ");
        PublicValues(variables).ShouldNotContain(value => value.Contains("s3cret"));
        variables.DeprecatedNames.ShouldBeEmpty();
        variables.CreateCredential(_fileSystem).ShouldBeOfType<ApiKeyCredential>().ClientCertificate.ShouldNotBeNull();
    }

    // Read checks the variables; the file is loaded by CreateCredential, for every credential it creates.
    [Fact]
    public void TheCertificateFile_LoadsInCreateCredential()
    {
        _variables["STORYTELLER_CLIENT_CERTIFICATE"] = "/run/missing.pfx";

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.Certificate);
        var exception = Should.Throw<StorytellerAuthenticationException>(() => variables.CreateCredential(_fileSystem));
        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("STORYTELLER_CLIENT_CERTIFICATE");
    }

    [Fact]
    public void ACertificatePasswordWithoutAFile_NamesTheVariableAsSet()
    {
        _variables["STORYTELLER_CERTIFICATE_PASSWORD"] = TestCertificates.Password;

        var exception = ShouldFail("STORYTELLER_CERTIFICATE_PASSWORD");

        exception.Message.ShouldBe("STORYTELLER_CERTIFICATE_PASSWORD is set without STORYTELLER_CLIENT_CERTIFICATE.");
    }

    [Fact]
    public void ClientCredentials()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;
        _variables["STORYTELLER_TOKEN_URL"] = "https://idp.example.com/token";
        _variables["STORYTELLER_TOKEN_SCOPE"] = "Configuration.Read";

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.ClientCredentials);
        variables.ClientId.ShouldBe("machine-client");
        variables.Names.ShouldBe(["STORYTELLER_CLIENT_ID", "STORYTELLER_CLIENT_SECRET", "STORYTELLER_TOKEN_URL", "STORYTELLER_TOKEN_SCOPE"]);
        variables.ToString().ShouldNotContain(ClientSecret);
        variables.CreateCredential(_fileSystem).ShouldBeOfType<ClientCredentialsCredential>();
    }

    // The token URL rule and the AuthKit rule are checked by Read, before any credential exists.
    [Theory]
    [InlineData("keycloak-machine", null)]
    [InlineData("machine-client", "http://idp.example.com/token")]
    public void ClientCredentials_WithoutAUsableTokenUrl_FailInRead(string clientId, string? tokenUrl)
    {
        _variables["STORYTELLER_CLIENT_ID"] = clientId;
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;
        _variables["STORYTELLER_TOKEN_URL"] = tokenUrl;

        ShouldFail("STORYTELLER_TOKEN_URL");
    }

    [Fact]
    public void ABearerTokenFile()
    {
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.TokenFile);
        variables.TokenFile.ShouldBe("/run/token");
        variables.ClientId.ShouldBeNull();
        variables.CreateCredential(_fileSystem).ShouldBeOfType<TokenFileCredential>();
    }

    [Fact]
    public void ATokenFileWithAClientId_IsFederation_FromTheFile()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "any-machine-id";
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.Federated);
        variables.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.File);
        variables.ClientId.ShouldBe("any-machine-id");
        variables.TokenFile.ShouldBeNull();
        variables.CreateCredential(_fileSystem).ToString().ShouldBe("StorytellerCredential { Kind = Federated, ClientId = any-machine-id, SubjectTokenPath = /run/token }");
    }

    // A platform token file wins over the runner: a job can write the token of another audience to a file.
    [Theory]
    [InlineData("STORYTELLER_TOKEN_FILE")]
    [InlineData("STORYTELLER_SUBJECT_TOKEN_FILE")]
    public void ATokenFile_WinsOverTheGitHubActionsVariables_WhoseUrlIsThenNotChecked(string fileVariable)
    {
        UseGitHubActions("http://runner.example.com/idtoken");
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;
        _variables[fileVariable] = "/run/token";

        Read().ShouldNotBeNull().SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.File);
    }

    // Never a bearer token file: the platform token would go to the API as Bearer on every request.
    [Fact]
    public void TheDeprecatedSubjectTokenFile_IsAlwaysFederation()
    {
        _variables["STORYTELLER_SUBJECT_TOKEN_FILE"] = "/run/token";

        ShouldFail("STORYTELLER_CLIENT_ID").Message.ShouldBe("A federated machine needs STORYTELLER_CLIENT_ID, the machine id.");

        _variables["STORYTELLER_CLIENT_ID"] = MachineId;
        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.Federated);
        variables.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.File);
        variables.DeprecatedNames.ShouldBe(["STORYTELLER_SUBJECT_TOKEN_FILE"]);
        variables.CreateCredential(_fileSystem).ShouldBeOfType<FederatedTokenCredential>().ToString().ShouldContain("SubjectTokenPath = /run/token");
    }

    [Fact]
    public void ATokenFileWithASubjectTokenType_AndNoClientId_AreTwoKinds()
    {
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "jwt";

        ShouldFail("different kinds").Message.ShouldBe(
            "Storyteller credential variables of different kinds are set: STORYTELLER_TOKEN_FILE, STORYTELLER_SUBJECT_TOKEN_TYPE. Set the variables of one kind only.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Configuration.Read")]
    public void AGuidClientId_InAGitHubActionsJob_IsFederation_ThroughTheRunner(string? scope)
    {
        UseGitHubActions();
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;
        _variables["STORYTELLER_TOKEN_SCOPE"] = scope;

        var variables = Read().ShouldNotBeNull();
        string[] names = scope is null ? ["STORYTELLER_CLIENT_ID"] : ["STORYTELLER_CLIENT_ID", "STORYTELLER_TOKEN_SCOPE"];

        variables.Kind.ShouldBe(StorytellerCredentialKind.Federated);
        variables.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.GitHubActions);
        variables.ClientId.ShouldBe(MachineId);
        variables.Names.ShouldBe(names);
        variables.ToString().ShouldBe($"StorytellerCredentialVariables {{ Kind = Federated, Names = {string.Join(", ", variables.Names)} }}");
        variables.CreateCredential(_fileSystem).ToString().ShouldBe($"StorytellerCredential {{ Kind = Federated, ClientId = {MachineId}, SubjectTokenSource = GitHubActions }}");
    }

    // An AuthKit or Keycloak client id without its secret (a missing GitHub secret expands to an empty value) must not
    // turn into an exchange.
    [Theory]
    [InlineData("client_01MACHINE")]
    [InlineData("42.sform.machine")]
    public void AClientIdThatIsNoGuid_InAGitHubActionsJob_NamesBothWays(string clientId)
    {
        UseGitHubActions();
        _variables["STORYTELLER_CLIENT_ID"] = clientId;
        _variables["STORYTELLER_CLIENT_SECRET"] = " ";

        ShouldFail("STORYTELLER_CLIENT_SECRET").Message.ShouldBe(
            "STORYTELLER_CLIENT_ID needs STORYTELLER_CLIENT_SECRET for client credentials, or a platform token for a federated machine: STORYTELLER_TOKEN_FILE, or a GitHub Actions job with permissions: id-token: write.");
    }

    [Theory]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_URL")]
    [InlineData("ACTIONS_ID_TOKEN_REQUEST_TOKEN")]
    public void AGuidClientId_WithOneGitHubActionsVariable_NamesBothWays(string missing)
    {
        UseGitHubActions();
        _variables.Remove(missing);
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;

        ShouldFail("STORYTELLER_CLIENT_SECRET").Message.ShouldContain("GitHub Actions");
    }

    // An Entra ID appId is a GUID too: with its secret, it stays client credentials in a job with id-token: write.
    [Theory]
    [InlineData(StorytellerCredentialKind.ApiKey, "STORYTELLER_API_KEY", ApiKey)]
    [InlineData(StorytellerCredentialKind.ClientCredentials, "STORYTELLER_CLIENT_SECRET", ClientSecret)]
    public void AnotherKind_InAGitHubActionsJob_StaysThatKind(StorytellerCredentialKind kind, string variable, string value)
    {
        UseGitHubActions();
        _variables[variable] = value;

        if (kind == StorytellerCredentialKind.ClientCredentials)
        {
            _variables["STORYTELLER_CLIENT_ID"] = MachineId;
            _variables["STORYTELLER_TOKEN_URL"] = "https://idp.example.com/token";
        }

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(kind);
        variables.SubjectTokenSource.ShouldBeNull();
        variables.Names.ShouldNotContain(name => name.StartsWith("ACTIONS_"));
    }

    [Fact]
    public void ATokenScopeWithoutAClientId_IsAnError_EvenInAGitHubActionsJob()
    {
        UseGitHubActions();
        _variables["STORYTELLER_TOKEN_SCOPE"] = "Configuration.Read";

        ShouldFail("STORYTELLER_TOKEN_SCOPE").Message.ShouldBe(
            "STORYTELLER_TOKEN_SCOPE needs STORYTELLER_CLIENT_ID, with STORYTELLER_CLIENT_SECRET or STORYTELLER_TOKEN_FILE.");
    }

    [Fact]
    public void AGuidClientId_InAGitHubActionsJob_WithAnotherKind_IsTwoKinds()
    {
        UseGitHubActions();
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;
        _variables["STORYTELLER_API_KEY"] = ApiKey;

        var exception = ShouldFail("different kinds");

        exception.Message.ShouldContain("STORYTELLER_CLIENT_ID");
        exception.Message.ShouldNotContain("ACTIONS_ID_TOKEN");
        exception.Message.ShouldNotContain(ApiKey);
    }

    // The type alone makes a federated machine of any id; the runner is its source when no file is set.
    [Fact]
    public void ASubjectTokenTypeWithoutAFile_UsesTheRunner_ForAnyClientId()
    {
        UseGitHubActions();
        _variables["STORYTELLER_CLIENT_ID"] = "any-machine-id";
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "ID_TOKEN";

        var variables = Read().ShouldNotBeNull();

        variables.Kind.ShouldBe(StorytellerCredentialKind.Federated);
        variables.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.GitHubActions);
    }

    [Fact]
    public void ASubjectTokenTypeWithoutAFile_NeedsAClientId()
    {
        UseGitHubActions();
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "jwt";

        ShouldFail("STORYTELLER_CLIENT_ID");
    }

    [Fact]
    public void ASubjectTokenTypeWithoutAFileOrARunner_NamesTheFileAndGitHubActions()
    {
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "jwt";

        ShouldFail("STORYTELLER_TOKEN_FILE").Message.ShouldBe(
            "A federated machine needs a platform token: set STORYTELLER_TOKEN_FILE to the file with it, or run in a GitHub Actions job with permissions: id-token: write.");
    }

    [Theory]
    [InlineData("http://runner.example.com/idtoken?api-version=2.0")]
    [InlineData("https://user:p4ssw0rd@runner.example.com/idtoken")]
    [InlineData("not a url")]
    public void ARunnerUrlThatBreaksTheUrlRule_NamesTheVariableButNotTheValue(string requestUrl)
    {
        UseGitHubActions(requestUrl);
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;

        var exception = ShouldFail("ACTIONS_ID_TOKEN_REQUEST_URL");

        exception.Message.ShouldNotContain("p4ssw0rd");
        exception.Message.ShouldNotContain("runner.example.com");
        exception.Message.ShouldNotContain(GitHubRequestToken);
    }

    [Fact]
    public void ALocalRunnerUrl_IsAllowed()
    {
        UseGitHubActions("http://localhost:8080/idtoken");
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;

        Read().ShouldNotBeNull().SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.GitHubActions);
    }

    [Theory]
    [InlineData("jwt")]
    [InlineData("Access_Token")]
    [InlineData("urn:ietf:params:oauth:token-type:id_token")]
    [InlineData("URN:IETF:PARAMS:OAUTH:TOKEN-TYPE:JWT")]
    public void TheSubjectTokenType_IsAShortFormOrAUrn_InAnyCase(string type)
    {
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = type;

        Read().ShouldNotBeNull().Kind.ShouldBe(StorytellerCredentialKind.Federated);
    }

    [Fact]
    public void ToString_NamesTheKindAndTheVariables_NeverAValue()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;
        _variables["STORYTELLER_TOKEN_URL"] = "https://u:p4ssw0rd@idp.example.com/token";

        ShouldFail("STORYTELLER_TOKEN_URL").Message.ShouldNotContain("p4ssw0rd");

        _variables["STORYTELLER_TOKEN_URL"] = "https://idp.example.com/token";
        var text = Read().ShouldNotBeNull().ToString();

        text.ShouldBe("StorytellerCredentialVariables { Kind = ClientCredentials, Names = STORYTELLER_CLIENT_ID, STORYTELLER_CLIENT_SECRET, STORYTELLER_TOKEN_URL }");
    }

    [Fact]
    public void TheGitHubCredential_PrintsNeitherTheRequestTokenNorTheRunnerUrl()
    {
        UseGitHubActions();
        _variables["STORYTELLER_CLIENT_ID"] = MachineId;

        var variables = Read().ShouldNotBeNull();
        var credential = variables.CreateCredential(_fileSystem).ShouldBeOfType<FederatedTokenCredential>();

        string[] texts = [variables.ToString(), credential.ToString()];
        texts.ShouldAllBe(text => !text.Contains(GitHubRequestToken) && !text.Contains("pipelines"));
    }

    [Fact]
    public void CreateCredential_MakesANewUnboundCredential_EveryTime()
    {
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";
        var variables = Read().ShouldNotBeNull();

        variables.CreateCredential(_fileSystem).ShouldNotBeSameAs(variables.CreateCredential(_fileSystem));
    }

    // The variables of a GitHub Actions job with permissions: id-token: write.
    private void UseGitHubActions(string requestUrl = GitHubRequestUrl)
    {
        _variables["ACTIONS_ID_TOKEN_REQUEST_URL"] = requestUrl;
        _variables["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = GitHubRequestToken;
    }

    private StorytellerCredentialVariables? Read()
    {
        return StorytellerCredentialVariables.Read(name => _variables.GetValueOrDefault(name));
    }

    // What a structured logger or a serializer would print: the public properties, not ToString.
    private static List<string> PublicValues(StorytellerCredentialVariables variables)
    {
        return typeof(StorytellerCredentialVariables)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.GetValue(variables))
            .SelectMany(value => value is IEnumerable<string> list ? list : [value?.ToString() ?? string.Empty])
            .ToList();
    }

    private StorytellerAuthenticationException ShouldFail(string text)
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(() => Read());

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain(text);
        exception.Message.ShouldNotContain(ApiKey);
        exception.Message.ShouldNotContain(ClientSecret);
        return exception;
    }
}
