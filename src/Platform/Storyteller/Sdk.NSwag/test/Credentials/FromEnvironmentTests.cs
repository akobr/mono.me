using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class FromEnvironmentTests
{
    private const string ApiKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ.k3y-s3cret";
    private const string ClientSecret = "cl13nt-s3cret";
    private const string CertificatePassword = TestCertificates.Password;

    private readonly Dictionary<string, string?> _variables = [];
    private readonly MockFileSystem _fileSystem = new();

    public FromEnvironmentTests()
    {
        _fileSystem.AddFile("/run/machine.pfx", new MockFileData(TestCertificates.Pkcs12()));
        _fileSystem.AddFile("/run/token", new MockFileData("token"));
    }

    [Fact]
    public void NoVariables_DescribeNoCredential()
    {
        _variables["STORYTELLER_ENDPOINT"] = "https://api.example.com/api";
        _variables["STORYTELLER_API_KEY"] = "   ";

        Read().ShouldBeNull();
    }

    [Fact]
    public async Task AnApiKey()
    {
        _variables["STORYTELLER_API_KEY"] = $"  {ApiKey}  ";

        var credential = Read().ShouldBeOfType<ApiKeyCredential>();

        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe(ApiKey);
        credential.ClientCertificate.ShouldBeNull();
    }

    [Fact]
    public void TheApiKeySchemeAlone_IsAConfigurationError()
    {
        _variables["STORYTELLER_API_KEY"] = "ApiKey ";

        ShouldFail("STORYTELLER_API_KEY");
    }

    [Theory]
    [InlineData("STORYTELLER_CLIENT_CERTIFICATE", "STORYTELLER_CLIENT_CERTIFICATE_PASSWORD")]
    [InlineData("STORYTELLER_CERTIFICATE_FILE", "STORYTELLER_CERTIFICATE_PASSWORD")]
    [InlineData("STORYTELLER_CLIENT_CERTIFICATE", "STORYTELLER_CERTIFICATE_PASSWORD")]
    public async Task ACertificate_UnderEitherName(string fileVariable, string passwordVariable)
    {
        _variables[fileVariable] = "/run/machine.pfx";
        _variables[passwordVariable] = CertificatePassword;

        var credential = Read().ShouldBeOfType<CertificateCredential>();

        credential.ClientCertificate.ShouldNotBeNull().HasPrivateKey.ShouldBeTrue();
        (await credential.GetAuthorizationAsync(null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ACertificateWithAnApiKey()
    {
        _variables["STORYTELLER_API_KEY"] = ApiKey;
        _variables["STORYTELLER_CLIENT_CERTIFICATE"] = "/run/machine.pfx";
        _variables["STORYTELLER_CLIENT_CERTIFICATE_PASSWORD"] = CertificatePassword;

        var credential = Read().ShouldBeOfType<ApiKeyCredential>();

        credential.ClientCertificate.ShouldNotBeNull();
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Scheme.ShouldBe("ApiKey");
    }

    [Fact]
    public void ACertificatePasswordWithoutAFile_IsAConfigurationError()
    {
        _variables["STORYTELLER_CERTIFICATE_PASSWORD"] = CertificatePassword;

        ShouldFail("STORYTELLER_CERTIFICATE_PASSWORD").Message.ShouldNotContain(CertificatePassword);
    }

    [Fact]
    public void ClientCredentials()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;
        _variables["STORYTELLER_TOKEN_URL"] = "https://idp.example.com/token";
        _variables["STORYTELLER_TOKEN_SCOPE"] = "Configuration.Read";

        var credential = Read().ShouldBeOfType<ClientCredentialsCredential>();

        credential.CanRenew.ShouldBeTrue();
        credential.ToString().ShouldNotContain(ClientSecret);
    }

    [Fact]
    public void ClientCredentials_OfAuthKit_NeedNoTokenUrl()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "client_01MACHINE";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;

        Read().ShouldBeOfType<ClientCredentialsCredential>();
    }

    [Fact]
    public void ClientCredentials_OutsideAuthKit_NeedATokenUrl()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "keycloak-machine";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;

        ShouldFail("STORYTELLER_TOKEN_URL");
    }

    [Fact]
    public void ClientCredentials_WithoutASecret_NameIt()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_TOKEN_URL"] = "https://idp.example.com/token";

        ShouldFail("STORYTELLER_CLIENT_SECRET").Message.ShouldBe(
            "Client credentials need STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET; STORYTELLER_CLIENT_SECRET is missing.");
    }

    [Fact]
    public void ClientCredentials_WithoutAClientId_NameIt()
    {
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;

        var exception = ShouldFail("STORYTELLER_CLIENT_ID");

        exception.Message.ShouldContain("is missing");
        exception.Message.ShouldNotContain(ClientSecret);
    }

    [Theory]
    [InlineData("http://idp.example.com/token")]
    [InlineData("https://user:password@idp.example.com/token")]
    [InlineData("not a url")]
    public void ATokenUrlThatBreaksTheLocalUrlRule_IsAConfigurationError(string tokenUrl)
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;
        _variables["STORYTELLER_TOKEN_URL"] = tokenUrl;

        ShouldFail("STORYTELLER_TOKEN_URL").Message.ShouldNotContain("password");
    }

    [Theory]
    [InlineData("STORYTELLER_TOKEN_FILE")]
    [InlineData("STORYTELLER_SUBJECT_TOKEN_FILE")]
    public void Federation_WithAClientIdAndATokenFile(string fileVariable)
    {
        _variables["STORYTELLER_CLIENT_ID"] = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
        _variables[fileVariable] = "/run/token";
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "access_token";

        var credential = Read().ShouldBeOfType<FederatedTokenCredential>();

        credential.ToString().ShouldContain("/run/token");
    }

    [Fact]
    public void Federation_AcceptsTheSameFileUnderBothNames()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";
        _variables["STORYTELLER_SUBJECT_TOKEN_FILE"] = "/run/token";

        Read().ShouldBeOfType<FederatedTokenCredential>();
    }

    [Fact]
    public void Federation_WithAnUnknownSubjectTokenType_IsAConfigurationError()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";
        _variables["STORYTELLER_SUBJECT_TOKEN_TYPE"] = "saml";

        ShouldFail("STORYTELLER_SUBJECT_TOKEN_TYPE");
    }

    [Fact]
    public void Federation_WithoutAClientId_IsAConfigurationError()
    {
        _variables["STORYTELLER_SUBJECT_TOKEN_FILE"] = "/run/token";

        ShouldFail("STORYTELLER_CLIENT_ID");
    }

    [Fact]
    public async Task ATokenFile_WithoutAClientId()
    {
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";

        var credential = Read().ShouldBeOfType<TokenFileCredential>();

        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe("token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Configuration.Read")]
    public void AClientIdAlone_NamesTheMissingVariables(string? scope)
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_TOKEN_SCOPE"] = scope;

        var exception = ShouldFail("STORYTELLER_CLIENT_SECRET");

        exception.Message.ShouldContain("STORYTELLER_TOKEN_FILE");
    }

    [Fact]
    public void ATokenScopeAlone_IsAConfigurationError()
    {
        _variables["STORYTELLER_TOKEN_SCOPE"] = "Configuration.Read";

        ShouldFail("STORYTELLER_CLIENT_ID");
    }

    [Theory]
    [InlineData("STORYTELLER_API_KEY", ApiKey, "STORYTELLER_TOKEN_FILE", "/run/token")]
    [InlineData("STORYTELLER_API_KEY", ApiKey, "STORYTELLER_CLIENT_SECRET", ClientSecret)]
    [InlineData("STORYTELLER_CERTIFICATE_FILE", "/run/machine.pfx", "STORYTELLER_SUBJECT_TOKEN_FILE", "/run/token")]
    [InlineData("STORYTELLER_TOKEN_FILE", "/run/token", "STORYTELLER_SUBJECT_TOKEN_FILE", "/run/token")]
    [InlineData("STORYTELLER_API_KEY", ApiKey, "STORYTELLER_TOKEN_SCOPE", "Configuration.Read")]
    [InlineData("STORYTELLER_API_KEY", ApiKey, "STORYTELLER_CLIENT_ID", "machine-client")]
    [InlineData("STORYTELLER_CLIENT_SECRET", ClientSecret, "STORYTELLER_SUBJECT_TOKEN_FILE", "/run/token")]
    [InlineData("STORYTELLER_TOKEN_URL", "https://idp.example.com/token", "STORYTELLER_SUBJECT_TOKEN_FILE", "/run/token")]
    [InlineData("STORYTELLER_CLIENT_SECRET", ClientSecret, "STORYTELLER_SUBJECT_TOKEN_TYPE", "jwt")]
    public void TwoKindsAtOnce_AreAConfigurationError_ThatNamesThem(string first, string firstValue, string second, string secondValue)
    {
        _variables[first] = firstValue;
        _variables[second] = secondValue;

        var exception = ShouldFail(first);

        exception.Message.ShouldContain(second);
        exception.Message.ShouldNotContain(ApiKey);
        exception.Message.ShouldNotContain(ClientSecret);
    }

    [Fact]
    public void ClientCredentialsWithATokenFile_AreTwoKinds()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "machine-client";
        _variables["STORYTELLER_CLIENT_SECRET"] = ClientSecret;
        _variables["STORYTELLER_TOKEN_URL"] = "https://idp.example.com/token";
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";

        ShouldFail("STORYTELLER_TOKEN_FILE").Message.ShouldContain("different kinds");
    }

    [Theory]
    [InlineData("STORYTELLER_CLIENT_CERTIFICATE", "STORYTELLER_CERTIFICATE_FILE")]
    [InlineData("STORYTELLER_CLIENT_CERTIFICATE_PASSWORD", "STORYTELLER_CERTIFICATE_PASSWORD")]
    public void ANameAndItsFallback_WithDifferentValues_AreAConfigurationError_ThatNamesBoth(string name, string fallback)
    {
        _variables["STORYTELLER_CLIENT_CERTIFICATE"] = "/run/machine.pfx";
        _variables[name] = "first-value";
        _variables[fallback] = "second-value";

        var exception = ShouldFail(name);

        exception.Message.ShouldContain(fallback);
        exception.Message.ShouldNotContain("first-value");
        exception.Message.ShouldNotContain("second-value");
    }

    [Fact]
    public void TheTokenFileAndTheSubjectTokenFile_WithDifferentValues_AreAConfigurationError()
    {
        _variables["STORYTELLER_CLIENT_ID"] = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/token";
        _variables["STORYTELLER_SUBJECT_TOKEN_FILE"] = "/run/other";

        ShouldFail("STORYTELLER_TOKEN_FILE").Message.ShouldContain("STORYTELLER_SUBJECT_TOKEN_FILE");
    }

    [Fact]
    public void ANameAndItsFallback_WithTheSameValue_Agree()
    {
        _variables["STORYTELLER_CLIENT_CERTIFICATE"] = "/run/machine.pfx";
        _variables["STORYTELLER_CERTIFICATE_FILE"] = "/run/machine.pfx";
        _variables["STORYTELLER_CERTIFICATE_PASSWORD"] = CertificatePassword;

        Read().ShouldBeOfType<CertificateCredential>();
    }

    [Fact]
    public void AMissingCertificateFile_NamesItsVariable()
    {
        _variables["STORYTELLER_CERTIFICATE_FILE"] = "/run/missing.pfx";

        ShouldFail("STORYTELLER_CERTIFICATE_FILE").Message.ShouldContain("/run/missing.pfx");
    }

    [Fact]
    public void AnEndpointAndItsFallback_WithDifferentValues_FailWhenTheCredentialNeedsThem()
    {
        _variables["STORYTELLER_ENDPOINT"] = "https://one.example.com/api";
        _variables["STORYTELLER_API_URL"] = "https://two.example.com/api";

        var exception = Should.Throw<StorytellerAuthenticationException>(() => UnboundTarget.ReadEndpoint(name => _variables.GetValueOrDefault(name)));

        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        exception.Message.ShouldContain("STORYTELLER_API_URL");
    }

    private StorytellerCredential? Read()
    {
        return StorytellerCredential.FromEnvironment(name => _variables.GetValueOrDefault(name), _fileSystem);
    }

    private StorytellerAuthenticationException ShouldFail(string variable)
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(() => Read());

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain(variable);
        return exception;
    }
}
