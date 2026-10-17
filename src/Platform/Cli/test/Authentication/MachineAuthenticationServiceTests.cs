using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class MachineAuthenticationServiceTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string TokenUrl = "https://auth.example.com/oauth2/token";
    private const string ClientSecret = "client-secret-value";

    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ScriptedHttpHandler _http = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));
    private readonly FakeResolver _resolver = new();
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.API_URL] = ApiUrl };

    [Fact]
    public async Task WithoutCredentialVariables_NothingIsConfigured()
    {
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        var service = CreateService();

        service.IsConfigured.ShouldBeFalse();
        service.TryGetIdentity().ShouldBeNull();
        service.TryGetCertificate().ShouldBeNull();
        (await service.GetAccessTokenAsync()).ShouldBeNull();
        (await service.GetSignedInUserAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task ApiKey_IsSentWithTheApiKeySchemeAndNamesTheMachine()
    {
        var key = TestApiKeys.Create("acme", "billing", "3f2c");
        _environment[StorytellerVariables.API_KEY] = key;
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe($"ApiKey {key}");
        service.GetIdentity().ShouldBe(new MachineIdentity(MachineSignInKind.ApiKey, [StorytellerVariables.API_KEY], "3f2c", "acme", "billing"), new IdentityComparer());
        (await service.GetSignedInUserAsync()).ShouldBe(new SignedInUser("3f2c", "machine 3f2c (acme.billing)", null));
        service.TryGetCertificate().ShouldBeNull();
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApiKey_InAnotherFormat_IsSentWithoutAnIdentity()
    {
        _environment[StorytellerVariables.API_KEY] = "legacy-key";
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe("ApiKey legacy-key");
        var identity = service.GetIdentity();
        identity.MachineId.ShouldBeNull();
        identity.Organization.ShouldBeNull();
        (await service.GetSignedInUserAsync()).ShouldBe(new SignedInUser("machine", "machine", null));
    }

    [Fact]
    public async Task Certificate_SendsNoTokenAndNamesTheMachineFromItsSan()
    {
        AddCertificate(TestCertificates.MachineUri("acme", "billing", "3f2c"));
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBeNull();
        var certificate = service.TryGetCertificate();
        certificate.ShouldNotBeNull();
        certificate.HasPrivateKey.ShouldBeTrue();
        var identity = service.GetIdentity();
        identity.Kind.ShouldBe(MachineSignInKind.Certificate);
        (identity.MachineId, identity.Organization, identity.Project).ShouldBe(("3f2c", "acme", "billing"));
    }

    [Fact]
    public async Task Certificate_WithAWrongPassword_IsAConfigurationError()
    {
        AddCertificate(TestCertificates.MachineUri("acme", "billing", "3f2c"));
        _environment[StorytellerVariables.CERTIFICATE_PASSWORD] = "wrong";
        var service = CreateService();

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("The certificate in STORYTELLER_CERTIFICATE_FILE could not be loaded; check the file and STORYTELLER_CERTIFICATE_PASSWORD.");
        exception.Message.ShouldNotContain("wrong");
        service.TryGetCertificate().ShouldBeNull();
        service.TryGetIdentity().ShouldBeNull();
    }

    [Fact]
    public async Task Certificate_ThatDoesNotExist_IsAConfigurationError()
    {
        _environment[StorytellerVariables.CERTIFICATE_FILE] = P(@"C:\ci\missing.pfx");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"The certificate file {P(@"C:\ci\missing.pfx")} in STORYTELLER_CERTIFICATE_FILE does not exist.");
    }

    [Fact]
    public async Task Certificate_WithoutAPrivateKey_IsNeverHandedToTheSdk()
    {
        _fileSystem.AddFile(P(@"C:\ci\machine.pfx"), new MockFileData(TestCertificates.Pkcs12WithoutPrivateKey(TestCertificates.MachineUri("acme", "billing", "3f2c"))));
        _environment[StorytellerVariables.CERTIFICATE_FILE] = P(@"C:\ci\machine.pfx");
        _environment[StorytellerVariables.CERTIFICATE_PASSWORD] = TestCertificates.Password;
        var service = CreateService();

        service.TryGetCertificate().ShouldBeNull();
        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());
        exception.Message.ShouldBe("The certificate in STORYTELLER_CERTIFICATE_FILE has no private key; use the .pfx file sform account machine create wrote.");
    }

    [Fact]
    public async Task SharedCertificate_Alone_IsRefused()
    {
        AddCertificate(TestCertificates.SharedUri("acme", "billing", "ci"));

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Message.ShouldBe("A shared certificate signs in only together with its machine's API key; set STORYTELLER_API_KEY too.");
    }

    [Fact]
    public async Task SharedCertificate_WithAKey_IsTheKeysMachine()
    {
        var key = TestApiKeys.Create("acme", "billing", "3f2c");
        AddCertificate(TestCertificates.SharedUri("acme", "billing", "ci"));
        _environment[StorytellerVariables.API_KEY] = key;
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe($"ApiKey {key}");
        service.TryGetCertificate().ShouldNotBeNull();
        var identity = service.GetIdentity();
        identity.Kind.ShouldBe(MachineSignInKind.CertificateAndApiKey);
        (identity.MachineId, identity.Organization, identity.Project).ShouldBe(("3f2c", "acme", "billing"));
    }

    [Fact]
    public async Task MachineCertificate_WithItsKey_SendsBoth()
    {
        var key = TestApiKeys.Create("acme", "billing", "3f2c");
        AddCertificate(TestCertificates.MachineUri("ACME", "billing", "3f2c"));
        _environment[StorytellerVariables.API_KEY] = key;
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe($"ApiKey {key}");
        service.TryGetCertificate().ShouldNotBeNull();
        service.GetIdentity().Organization.ShouldBe("ACME");
    }

    [Theory]
    [InlineData("acme", "web", "3f2c", "The certificate in STORYTELLER_CERTIFICATE_FILE belongs to acme.billing but the API key to acme.web.")]
    [InlineData("acme", "billing", "9a1b", "The certificate in STORYTELLER_CERTIFICATE_FILE belongs to machine 3f2c but the API key to machine 9a1b.")]
    public async Task CertificateAndKey_OfDifferentMachines_AreRefusedBeforeAnyCall(string organization, string project, string machineId, string message)
    {
        AddCertificate(TestCertificates.MachineUri("acme", "billing", "3f2c"));
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create(organization, project, machineId);
        var service = CreateService();

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Message.ShouldBe(message);
        service.TryGetCertificate().ShouldBeNull();
    }

    [Fact]
    public async Task ClientCredentials_ExchangesOnceAndRenewsShortlyBeforeExpiry()
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","token_type":"Bearer","expires_in":3600}""")
            .Respond(HttpStatusCode.OK, """{"access_token":"cc-2","token_type":"bearer","expires_in":3600}""");
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe("cc-1");
        _time.Now = Start.AddMinutes(58);
        (await service.GetAccessTokenAsync()).ShouldBe("cc-1");

        // Inside the 60-second margin, so the token is renewed.
        _time.Now = Start.AddMinutes(59.5);
        (await service.GetAccessTokenAsync()).ShouldBe("cc-2");

        _http.Requests.Count.ShouldBe(2);
        var request = _http.Requests[0];
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri(TokenUrl));
        request.Authorization.ShouldBeNull();
        request.Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "client_01",
            ["client_secret"] = ClientSecret,
        });
    }

    [Fact]
    public async Task ClientCredentials_SendTheScopeWhenItIsSet()
    {
        UseClientCredentials("6d0f2c4e-0000-4000-8000-000000000001");
        _environment[StorytellerVariables.TOKEN_SCOPE] = "api://storyteller/.default";
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        var identity = await CreateService().SignInAsync();

        _http.Requests[0].Form["scope"].ShouldBe("api://storyteller/.default");
        identity.MachineId.ShouldBe("6d0f2c4e-0000-4000-8000-000000000001");
        identity.Organization.ShouldBeNull();
        identity.TokenExpiresAt.ShouldBe(Start.AddHours(1));
    }

    [Theory]
    [InlineData(""", "expires_in":"120" """, 2)]
    [InlineData(""", "expires_in":9223372036854775807 """, 24 * 60)]
    [InlineData("", 5)]
    public async Task ClientCredentials_WithoutAUsableLifetime_FallBack(string lifetime, int minutes)
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, $$"""{"access_token":"cc-1"{{lifetime}}}""");

        (await CreateService().SignInAsync()).TokenExpiresAt.ShouldBe(Start.AddMinutes(minutes));
    }

    [Fact]
    public async Task ClientCredentials_WithoutALifetime_UseTheJwtExpiry()
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, $$"""{"access_token":"{{TestTokens.AccessToken(Start.AddMinutes(30))}}"}""");

        (await CreateService().SignInAsync()).TokenExpiresAt.ShouldBe(Start.AddMinutes(30));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_description":"Bad secret."}""", "The token request to https://auth.example.com/oauth2/token was rejected: invalid_client: Bad secret.")]
    [InlineData(HttpStatusCode.Unauthorized, """{"code":"invalid_client","message":"Unknown client"}""", "The token request to https://auth.example.com/oauth2/token was rejected: Unknown client.")]
    [InlineData(HttpStatusCode.Unauthorized, "", "The token request to https://auth.example.com/oauth2/token was rejected.")]
    public async Task ClientCredentials_Rejected_AreDenied(HttpStatusCode status, string body, string message)
    {
        UseClientCredentials("client_01");
        _http.Respond(status, body);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Denied);
        exception.Message.ShouldBe(message);
        exception.Message.ShouldNotContain(ClientSecret);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "", "The token request to https://auth.example.com/oauth2/token failed with 500.")]
    [InlineData(HttpStatusCode.Found, "", "The token request to https://auth.example.com/oauth2/token failed with 302.")]
    [InlineData(HttpStatusCode.OK, """{"access_token":"cc-1","token_type":"mac"}""", "The token endpoint https://auth.example.com/oauth2/token returned no bearer token.")]
    [InlineData(HttpStatusCode.OK, "{}", "The token endpoint https://auth.example.com/oauth2/token returned no bearer token.")]
    [InlineData(HttpStatusCode.OK, "<html/>", "The token endpoint https://auth.example.com/oauth2/token returned no bearer token.")]
    public async Task ClientCredentials_UnusableAnswers_AreServiceErrors(HttpStatusCode status, string body, string message)
    {
        UseClientCredentials("client_01");
        _http.Respond(status, body);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe(message);
    }

    [Fact]
    public async Task ClientCredentials_UnreachableEndpoint_IsAServiceError()
    {
        UseClientCredentials("client_01");
        _http.Fail(new HttpRequestException("No such host is known."));

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe("The token endpoint https://auth.example.com/oauth2/token could not be reached.");
    }

    [Fact]
    public async Task ClientCredentials_OfAnAuthKitClient_UseTheDiscoveredDomain()
    {
        UseClientCredentials("client_01", tokenUrl: null);
        _resolver.Answer = new ResolvedAuthentication { Provider = AuthenticationProvider.AuthKit, ClientId = "client_user", AuthKitDomain = "https://acme.authkit.app/" };
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        (await CreateService().GetAccessTokenAsync()).ShouldBe("cc-1");

        _http.Requests[0].Uri.ShouldBe(new Uri("https://acme.authkit.app/oauth2/token"));
    }

    [Theory]
    [InlineData("42.sform.acme.billing.0a1b", AuthenticationProvider.AuthKit, "https://acme.authkit.app", false)]
    [InlineData("client_01", AuthenticationProvider.EntraId, null, true)]
    [InlineData("client_01", AuthenticationProvider.AuthKit, "http://acme.authkit.app", true)]
    [InlineData("client_01", AuthenticationProvider.AuthKit, null, true)]
    public async Task ClientCredentials_WithoutATrustedTokenUrl_AskForIt(string clientId, AuthenticationProvider provider, string? domain, bool isDiscoveryAsked)
    {
        UseClientCredentials(clientId, tokenUrl: null);
        _resolver.Answer = new ResolvedAuthentication { Provider = provider, ClientId = "client_user", AuthKitDomain = domain };

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("Set STORYTELLER_TOKEN_URL to the token URL that sform account machine create printed.");
        _resolver.Resolutions.ShouldBe(isDiscoveryAsked ? 1 : 0);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClientCredentials_WhenDiscoveryFails_AskForTheTokenUrl()
    {
        UseClientCredentials("client_01", tokenUrl: null);
        _resolver.Failure = new AuthenticationException(AuthenticationFailureReason.ServiceError, "not discoverable");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Message.ShouldBe("Set STORYTELLER_TOKEN_URL to the token URL that sform account machine create printed.");
    }

    [Fact]
    public async Task TokenFile_IsReadBeforeEveryRequest()
    {
        var path = P(@"C:\ci\token");
        _environment[StorytellerVariables.TOKEN_FILE] = path;
        _fileSystem.AddFile(path, new MockFileData("Bearer token-1\n"));
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe("token-1");
        _fileSystem.File.WriteAllText(path, "token-2");
        (await service.GetAccessTokenAsync()).ShouldBe("token-2");

        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task TokenFile_SignIn_ReadsTheExpiryOfAJwt()
    {
        var path = P(@"C:\ci\token");
        _environment[StorytellerVariables.TOKEN_FILE] = path;
        _fileSystem.AddFile(path, new MockFileData(TestTokens.AccessToken(Start.AddMinutes(10))));

        var identity = await CreateService().SignInAsync();

        identity.Kind.ShouldBe(MachineSignInKind.TokenFile);
        identity.MachineId.ShouldBeNull();
        identity.TokenExpiresAt.ShouldBe(Start.AddMinutes(10));
    }

    [Fact]
    public async Task TokenFile_ThatHasExpired_FailsTheSignIn()
    {
        var path = P(@"C:\ci\token");
        _environment[StorytellerVariables.TOKEN_FILE] = path;
        _fileSystem.AddFile(path, new MockFileData(TestTokens.AccessToken(Start.AddMinutes(-1))));

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().SignInAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Expired);
        exception.Message.ShouldBe("The token in STORYTELLER_TOKEN_FILE expired at 2026-10-17T11:59:00.0000000+00:00.");
    }

    [Theory]
    [InlineData(null, "does not exist")]
    [InlineData("  \n", "is empty")]
    [InlineData("Bearer ", "is empty")]
    public async Task TokenFile_MissingOrEmpty_IsAConfigurationError(string? content, string problem)
    {
        var path = P(@"C:\ci\token");
        _environment[StorytellerVariables.TOKEN_FILE] = path;

        if (content is not null)
        {
            _fileSystem.AddFile(path, new MockFileData(content));
        }

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"The token file {path} in STORYTELLER_TOKEN_FILE {problem}.");
    }

    [Fact]
    public async Task InvalidVariables_FailEveryCallWithTheSameReason()
    {
        _environment.Remove(StorytellerVariables.API_URL);
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        var service = CreateService();

        var first = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());
        var second = await Should.ThrowAsync<AuthenticationException>(() => service.SignInAsync());

        first.Message.ShouldBe("Set STORYTELLER_API_URL to the API base URL, for example https://<host>/api.");
        second.Message.ShouldBe(first.Message);
        second.ShouldNotBeSameAs(first);
        service.TryGetIdentity().ShouldBeNull();
    }

    [Fact]
    public async Task DeviceCode_IsRefusedAndLogoutKeepsEverything()
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        var service = CreateService();

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.LoginWithDeviceCodeAsync(_ => Task.CompletedTask));
        await service.LogoutAsync();

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("sform signs in with the machine credentials in STORYTELLER_API_KEY; unset them to sign in as a user.");
        (await service.GetAccessTokenAsync()).ShouldStartWith("ApiKey ");
    }

    private static string P(string path) => MockUnixSupport.Path(path);

    private void AddCertificate(string spiffeUri)
    {
        var path = P(@"C:\ci\machine.pfx");
        _fileSystem.AddFile(path, new MockFileData(TestCertificates.Pkcs12(spiffeUri)));
        _environment[StorytellerVariables.CERTIFICATE_FILE] = path;
        _environment[StorytellerVariables.CERTIFICATE_PASSWORD] = TestCertificates.Password;
    }

    private void UseClientCredentials(string clientId, string? tokenUrl = TokenUrl)
    {
        _environment[StorytellerVariables.CLIENT_ID] = clientId;
        _environment[StorytellerVariables.CLIENT_SECRET] = ClientSecret;

        if (tokenUrl is not null)
        {
            _environment[StorytellerVariables.TOKEN_URL] = tokenUrl;
        }
    }

    private MachineAuthenticationService CreateService()
    {
        var settings = MachineSignInSettings.FromEnvironment(name => _environment.GetValueOrDefault(name));
        return new MachineAuthenticationService(settings, _fileSystem, new StubHttpClientFactory(_http), _resolver, _time);
    }

    private sealed class IdentityComparer : IEqualityComparer<MachineIdentity>
    {
        public bool Equals(MachineIdentity? x, MachineIdentity? y)
        {
            return x is not null && y is not null
                && x.Kind == y.Kind
                && string.Join(",", x.Variables) == string.Join(",", y.Variables)
                && x.MachineId == y.MachineId
                && x.Organization == y.Organization
                && x.Project == y.Project
                && x.TokenExpiresAt == y.TokenExpiresAt;
        }

        public int GetHashCode(MachineIdentity obj)
        {
            return HashCode.Combine(obj.Kind, obj.MachineId);
        }
    }

    private sealed class FakeResolver : IAuthenticationConfigurationResolver
    {
        public ResolvedAuthentication? Answer { get; set; }

        public Exception? Failure { get; set; }

        public int Resolutions { get; private set; }

        public Task<ResolvedAuthentication> ResolveAsync(CancellationToken cancellationToken = default)
        {
            Resolutions++;

            if (Failure is not null)
            {
                return Task.FromException<ResolvedAuthentication>(Failure);
            }

            return Task.FromResult(Answer ?? throw new InvalidOperationException("No discovery answer scripted."));
        }

        public Task ForgetAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
