using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
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
    private const string MachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string TokenEndpoint = "https://storyteller.example.com/api/v1/acme/billing/access/token";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string GitHubRequestToken = "github-request-token-value";
    private const string EscapedTokenEndpoint = "https%3A%2F%2Fstoryteller.example.com%2Fapi%2Fv1%2Facme%2Fbilling%2Faccess%2Ftoken";

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

    [Fact]
    public async Task Federated_ExchangesThePlatformTokenAtTheTokenEndpointOfTheProject()
    {
        UseFederatedFile("platform-token-1");
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri(TokenEndpoint));
        request.Authorization.ShouldBeNull();
        request.Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["client_id"] = MachineId,
            ["subject_token"] = "platform-token-1",
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:jwt",
        });
    }

    [Fact]
    public async Task Federated_EscapesTheTargetAndSendsTheScopeAndTheTokenType()
    {
        _environment[StorytellerVariables.API_URL] = $"{ApiUrl}/";
        _environment[StorytellerVariables.TOKEN_SCOPE] = "story:read annotation:write";
        _environment[StorytellerVariables.SUBJECT_TOKEN_TYPE] = "access_token";
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        var service = CreateService();
        service.BindTarget("acme corp", "billing.eu");

        await service.GetAccessTokenAsync();

        var request = _http.Requests.ShouldHaveSingleItem();
        request.Uri.AbsoluteUri.ShouldBe("https://storyteller.example.com/api/v1/acme%20corp/billing.eu/access/token");
        request.Form["scope"].ShouldBe("story:read annotation:write");
        request.Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:access_token");
    }

    [Fact]
    public async Task Federated_ReadsTheFileAgainForEveryExchange()
    {
        var path = UseFederatedFile("platform-token-1");
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");

        // A projected Kubernetes token rotates while the job runs.
        _fileSystem.File.WriteAllText(path, "platform-token-2\n");
        _time.Now = Start.AddMinutes(14);
        (await service.GetAccessTokenAsync()).ShouldBe("st-2");

        _http.Requests.Select(request => request.Form["subject_token"]).ShouldBe(["platform-token-1", "platform-token-2"]);
    }

    [Fact]
    public async Task Federated_ReusesA900SecondTokenUntil60SecondsBeforeItExpires()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");
        _time.Now = Start.AddMinutes(13).AddSeconds(59);
        (await service.GetAccessTokenAsync()).ShouldBe("st-1");
        _time.Now = Start.AddMinutes(14);
        (await service.GetAccessTokenAsync()).ShouldBe("st-2");

        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Federated_AJobOf40Minutes_KeepsGettingTokens()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-3"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        for (var minute = 0; minute <= 40; minute++)
        {
            _time.Now = Start.AddMinutes(minute);
            var identity = await service.SignInAsync();

            identity.TokenExpiresAt!.Value.ShouldBeGreaterThan(_time.Now.AddSeconds(60));
        }

        // At 0, 14 and 28 minutes; the last token is valid until 43.
        _http.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Federated_AnotherTarget_ExchangesAgain()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-billing"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-web"));
        var service = CreateService();

        service.BindTarget("acme", "billing");
        (await service.GetAccessTokenAsync()).ShouldBe("st-billing");
        service.BindTarget("acme", "web");
        (await service.GetAccessTokenAsync()).ShouldBe("st-web");

        _http.Requests.Select(request => request.Uri.AbsoluteUri).ShouldBe([TokenEndpoint, "https://storyteller.example.com/api/v1/acme/web/access/token"]);
    }

    [Fact]
    public async Task Federated_TheBoundTarget_WinsOverStorytellerProject()
    {
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-web"));
        var service = CreateService();
        service.BindTarget("acme", "web");

        var identity = await service.SignInAsync();

        (identity.Organization, identity.Project).ShouldBe(("acme", "web"));
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://storyteller.example.com/api/v1/acme/web/access/token"));
        (await service.GetAccessTokenAsync()).ShouldBe("st-web");
    }

    [Fact]
    public async Task Federated_WithoutABoundTarget_SendsNoTokenAndAsksNothing()
    {
        // sform account ca and the user-only commands bind no target.
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        UseFederatedGitHub();
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBeNull();
        (await service.RenewAccessTokenAsync("st-1")).ShouldBeNull();

        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Federated_SignIn_WithoutABoundTarget_UsesStorytellerProject()
    {
        _environment[StorytellerVariables.PROJECT] = "acme.billing";
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        var service = CreateService();

        var identity = await service.SignInAsync();

        identity.ShouldBe(
            new MachineIdentity(MachineSignInKind.Federated, [StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE], MachineId, "acme", "billing", Start.AddMinutes(15)),
            new IdentityComparer());
        (await service.GetSignedInUserAsync()).ShouldBe(new SignedInUser(MachineId, $"machine {MachineId} (acme.billing)", null));
        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri(TokenEndpoint));
    }

    [Fact]
    public async Task Federated_SignIn_WithoutAnyProject_IsAConfigurationError()
    {
        UseFederatedFile();

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().SignInAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("No project is set for the machine: set STORYTELLER_PROJECT=<organization>.<project> or pass -p.");
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, new[] { StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE })]
    [InlineData(true, new[] { StorytellerVariables.CLIENT_ID, GitHubActionsVariables.ID_TOKEN_REQUEST_URL, GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN })]
    public void Federated_Identity_NamesTheMachineAndTheVariablesButNoProject(bool isGitHub, string[] variables)
    {
        if (isGitHub)
        {
            UseFederatedGitHub();
        }
        else
        {
            UseFederatedFile();
        }

        var service = CreateService();
        service.BindTarget("acme", "billing");

        // The credential names no project, so no -p or STORYTELLER_PROJECT can conflict with it.
        service.GetIdentity().ShouldBe(new MachineIdentity(MachineSignInKind.Federated, variables, MachineId, null, null), new IdentityComparer());
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(GitHubRequestUrl, $"{GitHubRequestUrl}&audience={EscapedTokenEndpoint}")]
    [InlineData("https://token.actions.example.com/idtoken", $"https://token.actions.example.com/idtoken?audience={EscapedTokenEndpoint}")]
    public async Task GitHub_RequestsAnIdTokenForTheTokenEndpoint(string requestUrl, string expectedUrl)
    {
        UseFederatedGitHub(requestUrl);
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");

        _http.Requests.Count.ShouldBe(2);
        var gitHub = _http.Requests[0];
        gitHub.Method.ShouldBe(HttpMethod.Get);
        gitHub.Uri.AbsoluteUri.ShouldBe(expectedUrl);
        gitHub.Authorization.ShouldBe($"Bearer {GitHubRequestToken}");
        gitHub.Headers!["Accept"].ShouldBe("application/json");
        var exchange = _http.Requests[1];
        exchange.Uri.ShouldBe(new Uri(TokenEndpoint));
        exchange.Form["subject_token"].ShouldBe("gh-jwt-1");
        exchange.Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:jwt");
    }

    [Fact]
    public async Task GitHub_TheRequestTokenGoesToTheRunnerOnly()
    {
        UseFederatedGitHub();
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-2"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");

        // The ID token lives about 5 minutes, so every exchange gets a fresh one.
        _time.Now = Start.AddMinutes(14);
        (await service.GetAccessTokenAsync()).ShouldBe("st-2");

        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Post]);
        _http.Requests[3].Form["subject_token"].ShouldBe("gh-jwt-2");
        var exchanges = _http.Requests.Where(request => request.Method == HttpMethod.Post).ToList();
        exchanges.ShouldAllBe(request => request.Authorization == null);
        exchanges.ShouldAllBe(request => !request.Headers!.Values.Any(value => value.Contains(GitHubRequestToken)));
        exchanges.ShouldAllBe(request => !request.Form.Values.Contains(GitHubRequestToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "", "The GitHub Actions ID token request failed with 403.")]
    [InlineData(HttpStatusCode.Found, "", "The GitHub Actions ID token request failed with 302.")]
    [InlineData(HttpStatusCode.OK, """{"count":0}""", "The GitHub Actions ID token request returned no token.")]
    [InlineData(HttpStatusCode.OK, "<html/>", "The GitHub Actions ID token request returned no token.")]
    public async Task GitHub_UnusableAnswers_AreServiceErrors(HttpStatusCode status, string body, string message)
    {
        UseFederatedGitHub();
        _http.Respond(status, body);
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe(message);
        _http.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GitHub_UnreachableOrTimedOut_IsAServiceError(bool isTimeout)
    {
        UseFederatedGitHub();
        _http.Fail(isTimeout ? new TaskCanceledException("The request timed out.") : new HttpRequestException("No such host is known."));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe("The GitHub Actions ID token could not be requested.");
    }

    [Fact]
    public async Task Federated_InvalidRequest_IsDeniedWithoutTheSubjectToken()
    {
        UseFederatedFile("platform-token-secret");
        _http.Respond(HttpStatusCode.BadRequest, """{"error":"invalid_request","error_description":"The subject token was not accepted."}""");
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Denied);
        exception.Message.ShouldBe($"The token request to {TokenEndpoint} was rejected: invalid_request: The subject token was not accepted.");
        exception.Message.ShouldNotContain("platform-token-secret");
    }

    [Fact]
    public async Task GitHub_InvalidRequest_IsDeniedWithTheAudienceItWasRequestedFor()
    {
        UseFederatedGitHub();
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-secret"))
            .Respond(HttpStatusCode.BadRequest, """{"error":"invalid_request","error_description":"The subject token was not accepted."}""");
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Denied);
        exception.Message.ShouldBe(
            $"The token request to {TokenEndpoint} was rejected: invalid_request: The subject token was not accepted; the GitHub Actions token of this job was requested for the audience {TokenEndpoint}, which a rule of the machine must name exactly. For client credentials, set STORYTELLER_CLIENT_SECRET instead.");
        exception.Message.ShouldNotContain("gh-jwt-secret");
        exception.Message.ShouldNotContain(GitHubRequestToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable"}""", $"The token request to {TokenEndpoint} failed with 503.")]
    [InlineData(HttpStatusCode.Found, "", $"The token request to {TokenEndpoint} failed with 302.")]
    [InlineData(HttpStatusCode.OK, "{}", $"The token endpoint {TokenEndpoint} returned no bearer token.")]
    public async Task Federated_UnusableAnswers_AreServiceErrors(HttpStatusCode status, string body, string message)
    {
        UseFederatedFile();
        _http.Respond(status, body);
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe(message);
    }

    [Fact]
    public async Task Federated_UnreachableEndpoint_IsAServiceError()
    {
        UseFederatedFile();
        _http.Fail(new HttpRequestException("No such host is known."));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe($"The token endpoint {TokenEndpoint} could not be reached.");
    }

    [Theory]
    [InlineData(null, "does not exist")]
    [InlineData("  \n", "is empty")]
    public async Task Federated_MissingOrEmptyFile_IsAConfigurationError(string? content, string problem)
    {
        var path = P(@"C:\ci\platform-token");
        _environment[StorytellerVariables.CLIENT_ID] = MachineId;
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = path;

        if (content is not null)
        {
            _fileSystem.AddFile(path, new MockFileData(content));
        }

        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"The platform token file {path} in STORYTELLER_SUBJECT_TOKEN_FILE {problem}.");
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Renew_OfTheCachedToken_ExchangesAgain()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");
        (await service.RenewAccessTokenAsync("st-1")).ShouldBe("st-2");
        (await service.GetAccessTokenAsync()).ShouldBe("st-2");

        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Renew_OfAnOlderToken_KeepsTheNewerOne()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");
        (await service.RenewAccessTokenAsync("st-1")).ShouldBe("st-2");

        // A late 401 of a request that still carried st-1.
        (await service.RenewAccessTokenAsync("st-1")).ShouldBe("st-2");

        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Renew_ABurstOfRejections_ExchangesOnce()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        var held = new HeldHttpHandler(_http);
        var service = CreateService(held);
        service.BindTarget("acme", "billing");
        held.Release();
        (await service.GetAccessTokenAsync()).ShouldBe("st-1");
        held.Hold();

        var renewals = Enumerable.Range(0, 5).Select(_ => service.RenewAccessTokenAsync("st-1")).ToList();
        held.Release();

        (await Task.WhenAll(renewals)).ShouldAllBe(token => token == "st-2");
        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Renew_IsOnlyForAFederatedMachine()
    {
        (await CreateService().RenewAccessTokenAsync("token")).ShouldBeNull();

        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        var apiKey = CreateService();
        apiKey.BindTarget("acme", "billing");
        (await apiKey.RenewAccessTokenAsync((await apiKey.GetAccessTokenAsync())!)).ShouldBeNull();

        _environment.Remove(StorytellerVariables.API_KEY);
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");
        var clientCredentials = CreateService();
        clientCredentials.BindTarget("acme", "billing");
        (await clientCredentials.GetAccessTokenAsync()).ShouldBe("cc-1");
        (await clientCredentials.RenewAccessTokenAsync("cc-1")).ShouldBeNull();
        (await clientCredentials.GetAccessTokenAsync()).ShouldBe("cc-1");

        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri(TokenUrl));
    }

    [Fact]
    public async Task Federated_ConcurrentCalls_ExchangeOnce()
    {
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        var held = new HeldHttpHandler(_http);
        var service = CreateService(held);
        service.BindTarget("acme", "billing");

        var calls = Enumerable.Range(0, 5).Select(_ => service.GetAccessTokenAsync()).ToList();
        held.Release();

        (await Task.WhenAll(calls)).ShouldAllBe(token => token == "st-1");
        _http.Requests.ShouldHaveSingleItem();
    }

    private static string P(string path) => MockUnixSupport.Path(path);

    private static string ExchangeAnswer(string accessToken)
    {
        return $$"""{"access_token":"{{accessToken}}","issued_token_type":"urn:ietf:params:oauth:token-type:access_token","token_type":"Bearer","expires_in":900,"scope":"story:read"}""";
    }

    private static string GitHubAnswer(string idToken)
    {
        return $$"""{"count":1,"value":"{{idToken}}"}""";
    }

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

    private string UseFederatedFile(string content = "platform-token")
    {
        var path = P(@"C:\ci\platform-token");
        _fileSystem.AddFile(path, new MockFileData(content));
        _environment[StorytellerVariables.CLIENT_ID] = MachineId;
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = path;
        return path;
    }

    // The variables of a GitHub Actions job with permissions: id-token: write.
    private void UseFederatedGitHub(string requestUrl = GitHubRequestUrl)
    {
        _environment[StorytellerVariables.CLIENT_ID] = MachineId;
        _environment[GitHubActionsVariables.ID_TOKEN_REQUEST_URL] = requestUrl;
        _environment[GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN] = GitHubRequestToken;
    }

    private MachineAuthenticationService CreateService(HttpMessageHandler? handler = null)
    {
        var settings = MachineSignInSettings.FromEnvironment(name => _environment.GetValueOrDefault(name));
        return new MachineAuthenticationService(settings, _fileSystem, new StubHttpClientFactory(handler ?? _http), _resolver, _time);
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

    // Holds every request until released, so that calls overlap while one exchange is in flight.
    private sealed class HeldHttpHandler : DelegatingHandler
    {
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HeldHttpHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public void Hold()
        {
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Release()
        {
            _release.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _release.Task;
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
