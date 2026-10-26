using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

// The service on the SDK credential. The token requests, the exchange and the GitHub Actions request themselves are
// tested in Sdk.NSwag/test/Credentials; these tests cover what sform adds: the identity, the target, the expiry of
// sform account and the mapping of the SDK's failures to sform's reasons and exit codes.
public class MachineAuthenticationServiceTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string TokenUrl = "https://auth.example.com/oauth2/token";
    private const string ClientSecret = "client-secret-value";
    private const string MachineId = "6d0f2c4e-0000-4000-8000-000000000001";
    private const string TokenEndpoint = "https://storyteller.example.com/api/v1/acme/billing/access/token";
    private const string DiscoveryUrl = "https://storyteller.example.com/api/v1/auth/configuration";
    private const string GitHubRequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string GitHubRunner = "https://pipelines.actions.githubusercontent.com/abc/idtoken";
    private const string GitHubRequestToken = "github-request-token-value";

    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ScriptedHttpHandler _http = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.ENDPOINT] = ApiUrl };

    [Fact]
    public async Task WithoutCredentialVariables_NothingIsConfigured()
    {
        _environment[TargetVariables.PROJECT] = "acme.billing";
        var service = CreateService();

        service.IsConfigured.ShouldBeFalse();
        service.TryGetIdentity().ShouldBeNull();
        service.TryGetCertificate().ShouldBeNull();
        (await service.GetAccessTokenAsync()).ShouldBeNull();
        (await service.GetSignedInUserAsync()).ShouldBeNull();
        (await service.RenewAccessTokenAsync("token")).ShouldBeNull();
    }

    [Fact]
    public async Task ApiKey_IsSentWithTheApiKeySchemeAndNamesTheMachine()
    {
        var key = TestApiKeys.Create("acme", "billing", "3f2c");
        _environment[StorytellerVariables.API_KEY] = $"ApiKey {key}";
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

    // CreateCredential loads the certificate: its failure keeps the message and the inner exception.
    [Fact]
    public async Task Certificate_WithAWrongPassword_IsAConfigurationError()
    {
        var path = AddCertificate(TestCertificates.MachineUri("acme", "billing", "3f2c"));
        _environment[StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD] = "wrong";
        var service = CreateService();

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"The certificate file {path} in STORYTELLER_CLIENT_CERTIFICATE could not be loaded; check the file and its password.");
        exception.Message.ShouldNotContain("wrong");
        exception.InnerException.ShouldBeAssignableTo<CryptographicException>();
        service.TryGetCertificate().ShouldBeNull();
        service.TryGetIdentity().ShouldBeNull();
    }

    [Fact]
    public async Task Certificate_ThatDoesNotExist_NamesTheVariableAsSet()
    {
        _environment[StorytellerVariables.CERTIFICATE_FILE] = P(@"C:\ci\missing.pfx");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"The certificate file {P(@"C:\ci\missing.pfx")} in STORYTELLER_CERTIFICATE_FILE does not exist.");
    }

    [Fact]
    public async Task Certificate_WithoutAPrivateKey_IsNeverHandedToTheSdk()
    {
        var path = P(@"C:\ci\machine.pfx");
        _fileSystem.AddFile(path, new MockFileData(TestCertificates.Pkcs12WithoutPrivateKey(TestCertificates.MachineUri("acme", "billing", "3f2c"))));
        _environment[StorytellerVariables.CLIENT_CERTIFICATE] = path;
        _environment[StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD] = TestCertificates.Password;
        var service = CreateService();

        service.TryGetCertificate().ShouldBeNull();
        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());
        exception.Message.ShouldBe($"The certificate in {path} in STORYTELLER_CLIENT_CERTIFICATE has no private key; use the .pfx file that sform account machine create wrote.");
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
    [InlineData(StorytellerVariables.CLIENT_CERTIFICATE, "acme", "web", "3f2c", "The certificate in STORYTELLER_CLIENT_CERTIFICATE belongs to acme.billing but the API key to acme.web.")]
    [InlineData(StorytellerVariables.CLIENT_CERTIFICATE, "acme", "billing", "9a1b", "The certificate in STORYTELLER_CLIENT_CERTIFICATE belongs to machine 3f2c but the API key to machine 9a1b.")]
    [InlineData(StorytellerVariables.CERTIFICATE_FILE, "acme", "billing", "9a1b", "The certificate in STORYTELLER_CERTIFICATE_FILE belongs to machine 3f2c but the API key to machine 9a1b.")]
    public async Task CertificateAndKey_OfDifferentMachines_AreRefusedBeforeAnyCall(string fileVariable, string organization, string project, string machineId, string message)
    {
        AddCertificate(TestCertificates.MachineUri("acme", "billing", "3f2c"), fileVariable);
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create(organization, project, machineId);
        var service = CreateService();

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Message.ShouldBe(message);
        service.TryGetCertificate().ShouldBeNull();
    }

    [Fact]
    public async Task ClientCredentials_RequestOnceAndRenewShortlyBeforeExpiry()
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

        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri, request.Authorization))
            .ShouldBe([(HttpMethod.Post, TokenUrl, null), (HttpMethod.Post, TokenUrl, null)]);
    }

    // sform account shows the exp of the token; the lifetime that renews it is the SDK's (expires_in first).
    [Fact]
    public async Task ClientCredentials_SignIn_ShowsTheExpiryOfAJwt()
    {
        UseClientCredentials(MachineId);
        _environment[StorytellerVariables.TOKEN_SCOPE] = "api://storyteller/.default";
        _http.Respond(HttpStatusCode.OK, $$"""{"access_token":"{{TestTokens.AccessToken(Start.AddMinutes(30))}}","expires_in":3600}""");

        var identity = await CreateService().SignInAsync();

        _http.Requests[0].Form["scope"].ShouldBe("api://storyteller/.default");
        identity.MachineId.ShouldBe(MachineId);
        identity.Organization.ShouldBeNull();
        identity.TokenExpiresAt.ShouldBe(Start.AddMinutes(30));
    }

    [Fact]
    public async Task ClientCredentials_WithAnOpaqueToken_HaveNoKnownExpiry()
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        (await CreateService().SignInAsync()).TokenExpiresAt.ShouldBeNull();
    }

    // Change against today's sform: a 403, a 404 and an answer without a bearer token are refusals (44), not service
    // errors (3).
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_description":"Bad secret."}""", "The token request to https://auth.example.com/oauth2/token was rejected with 400: invalid_client: Bad secret.")]
    [InlineData(HttpStatusCode.Unauthorized, """{"code":"invalid_client","message":"Unknown client"}""", "The token request to https://auth.example.com/oauth2/token was rejected with 401: Unknown client.")]
    [InlineData(HttpStatusCode.Unauthorized, "", "The token request to https://auth.example.com/oauth2/token was rejected with 401.")]
    [InlineData(HttpStatusCode.Forbidden, "", "The token request to https://auth.example.com/oauth2/token was rejected with 403.")]
    [InlineData(HttpStatusCode.NotFound, "", "The token request to https://auth.example.com/oauth2/token was rejected with 404.")]
    [InlineData(HttpStatusCode.Found, "", "The token request to https://auth.example.com/oauth2/token was answered with a redirect (302), which is not followed.")]
    [InlineData(HttpStatusCode.OK, """{"access_token":"cc-1","token_type":"mac"}""", "The token endpoint https://auth.example.com/oauth2/token returned no bearer token.")]
    [InlineData(HttpStatusCode.OK, "{}", "The token endpoint https://auth.example.com/oauth2/token returned no bearer token.")]
    [InlineData(HttpStatusCode.OK, "<html/>", "The token endpoint https://auth.example.com/oauth2/token returned no bearer token.")]
    public async Task ClientCredentials_Refused_AreDenied(HttpStatusCode status, string body, string message)
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
    [InlineData(HttpStatusCode.TooManyRequests, "", "The token request to https://auth.example.com/oauth2/token failed with 429.")]
    public async Task ClientCredentials_UnavailableEndpoint_IsAServiceError(HttpStatusCode status, string body, string message)
    {
        UseClientCredentials("client_01");
        _http.Respond(status, body);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe(message);
    }

    [Fact]
    public async Task ClientCredentials_UnreachableEndpoint_IsAServiceErrorWithItsCause()
    {
        UseClientCredentials("client_01");
        var cause = new HttpRequestException("No such host is known.");
        _http.Fail(cause);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.ServiceError);
        exception.Message.ShouldBe("The token request to https://auth.example.com/oauth2/token failed: No such host is known.");
        exception.InnerException.ShouldBeSameAs(cause);
    }

    // Change against today's sform: token requests are retried after a transient failure.
    [Fact]
    public async Task ClientCredentials_ATransientFailure_IsRetried()
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.ServiceUnavailable, string.Empty)
            .Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");
        var service = CreateService(resilience: new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero });

        (await service.GetAccessTokenAsync()).ShouldBe("cc-1");

        _http.Requests.Count.ShouldBe(2);
    }

    // Change against today's sform: a renewal that fails before the token expires keeps the token.
    [Fact]
    public async Task ClientCredentials_AFailedEarlyRenewal_KeepsTheToken()
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""")
            .Respond(HttpStatusCode.ServiceUnavailable, string.Empty);
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBe("cc-1");
        _time.Now = Start.AddMinutes(59.5);
        (await service.GetAccessTokenAsync()).ShouldBe("cc-1");

        _http.Requests.Count.ShouldBe(2);
    }

    // Change against today's sform: discovery asks the endpoint directly; authentication.provider and the
    // auth.discovery.json cache no longer play a part for a machine.
    [Fact]
    public async Task ClientCredentials_OfAnAuthKitClient_DiscoverTheTokenUrlAtTheEndpoint()
    {
        UseClientCredentials("client_01", tokenUrl: null);
        _http.Respond(HttpStatusCode.OK, """{"Provider":"AuthKit","AuthKitDomain":"https://acme.authkit.app/"}""")
            .Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");

        (await CreateService().GetAccessTokenAsync()).ShouldBe("cc-1");

        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri, request.Authorization)).ShouldBe(
        [
            (HttpMethod.Get, DiscoveryUrl, null),
            (HttpMethod.Post, "https://acme.authkit.app/oauth2/token", null),
        ]);
    }

    [Fact]
    public async Task ClientCredentials_OutsideAuthKit_WithoutATokenUrl_AskForItBeforeAnyRequest()
    {
        UseClientCredentials("42.sform.acme.billing.0a1b", tokenUrl: null);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("Set STORYTELLER_TOKEN_URL to the token URL that sform account machine create printed; only an AuthKit client id (client_…) discovers it.");
        _http.Requests.ShouldBeEmpty();
    }

    // Change against today's sform: a platform that is not AuthKit, or a domain that cannot be trusted, is a refusal
    // (44, was 2); a refused discovery is 44, an unavailable one 3.
    [Theory]
    [InlineData(HttpStatusCode.OK, """{"Provider":"EntraId"}""", AuthenticationFailureReason.Denied, "The platform at https://storyteller.example.com/api signs in with EntraId, not AuthKit, so the token URL of client_01 cannot be discovered; set the token URL.")]
    [InlineData(HttpStatusCode.OK, """{"Provider":"AuthKit","AuthKitDomain":"http://acme.authkit.app"}""", AuthenticationFailureReason.Denied, "The platform at https://storyteller.example.com/api names no https AuthKit domain without a path; set the token URL.")]
    [InlineData(HttpStatusCode.OK, """{"Provider":"AuthKit","AuthKitDomain":"https://acme.authkit.app/tenant"}""", AuthenticationFailureReason.Denied, "The platform at https://storyteller.example.com/api names no https AuthKit domain without a path; set the token URL.")]
    [InlineData(HttpStatusCode.OK, """{"Provider":"AuthKit"}""", AuthenticationFailureReason.Denied, "The platform at https://storyteller.example.com/api names no https AuthKit domain without a path; set the token URL.")]
    [InlineData(HttpStatusCode.NotFound, "", AuthenticationFailureReason.Denied, $"The sign-in discovery at {DiscoveryUrl} was rejected with 404.")]
    [InlineData(HttpStatusCode.BadGateway, "", AuthenticationFailureReason.ServiceError, $"The sign-in discovery at {DiscoveryUrl} failed with 502.")]
    public async Task ClientCredentials_WhenDiscoveryCannotHelp_SayWhy(HttpStatusCode status, string body, AuthenticationFailureReason reason, string message)
    {
        UseClientCredentials("client_01", tokenUrl: null);
        _http.Respond(status, body);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => CreateService().GetAccessTokenAsync());

        exception.Reason.ShouldBe(reason);
        exception.Message.ShouldBe(message);
        _http.Requests.ShouldHaveSingleItem().Uri.AbsoluteUri.ShouldBe(DiscoveryUrl);
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
    [InlineData("Bearer ", "holds no token")]
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
        _environment.Remove(StorytellerVariables.ENDPOINT);
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        var service = CreateService();

        var first = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());
        var second = await Should.ThrowAsync<AuthenticationException>(() => service.SignInAsync());

        first.Message.ShouldBe("Set STORYTELLER_ENDPOINT to the API base URL, for example https://<host>/api.");
        second.Message.ShouldBe(first.Message);
        second.ShouldNotBeSameAs(first);
        service.TryGetIdentity().ShouldBeNull();
    }

    // Read: an error of the shared contract is a configuration error, with its message.
    [Fact]
    public async Task VariablesTheSdkRefuses_AreAConfigurationError()
    {
        _environment[StorytellerVariables.API_KEY] = "ApiKey";
        var service = CreateService();

        service.IsConfigured.ShouldBeTrue();
        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe("STORYTELLER_API_KEY holds no key.");
        _http.Requests.ShouldBeEmpty();
    }

    // Bind: whatever the binding of the credential throws is mapped too.
    [Theory]
    [InlineData(false, AuthenticationFailureReason.Configuration)]
    [InlineData(true, AuthenticationFailureReason.ServiceError)]
    public async Task AFailureOfTheBinding_IsMapped(bool isSdkFailure, AuthenticationFailureReason reason)
    {
        UseClientCredentials("client_01");
        Exception failure = isSdkFailure
            ? new StorytellerAuthenticationException(StorytellerAuthenticationFailureReason.Unavailable, "The binding failed.", new TimeoutException())
            : new ArgumentException("The binding failed.");
        var hooks = new MachineCredentialHooks { Decorate = _ => throw failure };
        var service = TestMachines.Create(TestMachines.Settings(_environment), _fileSystem, hooks, _time);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(reason);
        exception.Message.ShouldBe("The binding failed.");
        exception.InnerException.ShouldBeSameAs(isSdkFailure ? failure.InnerException : failure);
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

    // Change against today's sform: STORYTELLER_TOKEN_FILE with STORYTELLER_CLIENT_ID is federation (was exit 2).
    [Fact]
    public async Task Federated_ATokenFileWithAClientId_ExchangesThePlatformTokenAtTheTokenEndpointOfTheProject()
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
        request.Form["client_id"].ShouldBe(MachineId);
        request.Form["subject_token"].ShouldBe("platform-token-1");
    }

    [Fact]
    public async Task Federated_EscapesTheTargetAndSendsTheScopeAndTheTokenType()
    {
        _environment[StorytellerVariables.ENDPOINT] = $"{ApiUrl}/";
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
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer(TestTokens.AccessToken(Start.AddMinutes(15))))
            .Respond(HttpStatusCode.OK, ExchangeAnswer(TestTokens.AccessToken(Start.AddMinutes(29))))
            .Respond(HttpStatusCode.OK, ExchangeAnswer(TestTokens.AccessToken(Start.AddMinutes(43))));
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
        _environment[TargetVariables.PROJECT] = "acme.billing";
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
        _environment[TargetVariables.PROJECT] = "acme.billing";
        UseFederatedGitHub();
        var service = CreateService();

        (await service.GetAccessTokenAsync()).ShouldBeNull();
        (await service.RenewAccessTokenAsync("st-1")).ShouldBeNull();

        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Federated_SignIn_WithoutABoundTarget_UsesStorytellerProject()
    {
        _environment[TargetVariables.PROJECT] = "acme.billing";
        UseFederatedFile();
        _http.Respond(HttpStatusCode.OK, ExchangeAnswer(TestTokens.AccessToken(Start.AddMinutes(15))));
        var service = CreateService();

        var identity = await service.SignInAsync();

        identity.ShouldBe(
            new MachineIdentity(MachineSignInKind.Federated, [StorytellerVariables.CLIENT_ID, StorytellerVariables.TOKEN_FILE], MachineId, "acme", "billing", Start.AddMinutes(15)),
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
    [InlineData("file", new[] { StorytellerVariables.CLIENT_ID, StorytellerVariables.TOKEN_FILE })]
    [InlineData("deprecated file", new[] { StorytellerVariables.CLIENT_ID, StorytellerVariables.SUBJECT_TOKEN_FILE })]
    [InlineData("GitHub Actions", new[] { StorytellerVariables.CLIENT_ID, StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL, StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN })]
    public void Federated_Identity_NamesTheMachineAndTheVariablesButNoProject(string source, string[] variables)
    {
        switch (source)
        {
            case "GitHub Actions":
                UseFederatedGitHub();
                break;
            case "deprecated file":
                UseFederatedFile(fileVariable: StorytellerVariables.SUBJECT_TOKEN_FILE);
                break;
            default:
                UseFederatedFile();
                break;
        }

        var service = CreateService();
        service.BindTarget("acme", "billing");

        // The credential names no project, so no -p or STORYTELLER_PROJECT can conflict with it.
        service.GetIdentity().ShouldBe(new MachineIdentity(MachineSignInKind.Federated, variables, MachineId, null, null), new IdentityComparer());
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHub_AsksTheRunnerForTheTokenEndpoint_AndExchanges()
    {
        UseFederatedGitHub();
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        var service = CreateService();
        service.BindTarget("acme", "billing");

        (await service.GetAccessTokenAsync()).ShouldBe("st-1");

        _http.Requests.Select(request => (request.Method, request.Uri.AbsoluteUri, request.Authorization)).ShouldBe(
        [
            (HttpMethod.Get, $"{GitHubRequestUrl}&audience={Uri.EscapeDataString(TokenEndpoint)}", $"Bearer {GitHubRequestToken}"),
            (HttpMethod.Post, TokenEndpoint, null),
        ]);
        _http.Requests[1].Form["subject_token"].ShouldBe("gh-jwt-1");
    }

    // Change against today's sform: a refusing runner (3xx, 4xx, or no token) is 44, was 3; 5xx and network failures stay 3.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "", AuthenticationFailureReason.Denied, $"The GitHub Actions ID token request to {GitHubRunner} was rejected with 403.")]
    [InlineData(HttpStatusCode.Found, "", AuthenticationFailureReason.Denied, $"The GitHub Actions ID token request to {GitHubRunner} was answered with a redirect (302), which is not followed.")]
    [InlineData(HttpStatusCode.OK, """{"count":0}""", AuthenticationFailureReason.Denied, $"The GitHub Actions ID token request to {GitHubRunner} returned no token.")]
    [InlineData(HttpStatusCode.OK, "<html/>", AuthenticationFailureReason.Denied, $"The GitHub Actions ID token request to {GitHubRunner} returned no token.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", AuthenticationFailureReason.ServiceError, $"The GitHub Actions ID token request to {GitHubRunner} failed with 503.")]
    public async Task GitHub_UnusableAnswers_SayWhy(HttpStatusCode status, string body, AuthenticationFailureReason reason, string message)
    {
        UseFederatedGitHub();
        _http.Respond(status, body);
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(reason);
        exception.Message.ShouldBe(message);
        exception.Message.ShouldNotContain(GitHubRequestToken);
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
        exception.Message.ShouldStartWith($"The GitHub Actions ID token request to {GitHubRunner} failed: ");
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
        exception.Message.ShouldBe($"The token request to {TokenEndpoint} was rejected with 400: invalid_request: The subject token was not accepted.");
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
            $"The token request to {TokenEndpoint} was rejected with 400: invalid_request: The subject token was not accepted; the GitHub Actions token of this job was requested for the audience {TokenEndpoint}, which a rule of the machine must name exactly. For client credentials, set STORYTELLER_CLIENT_SECRET instead.");
        exception.Message.ShouldNotContain("gh-jwt-secret");
        exception.Message.ShouldNotContain(GitHubRequestToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable"}""", AuthenticationFailureReason.ServiceError, $"The token request to {TokenEndpoint} failed with 503: temporarily_unavailable.")]
    [InlineData(HttpStatusCode.Found, "", AuthenticationFailureReason.Denied, $"The token request to {TokenEndpoint} was answered with a redirect (302), which is not followed.")]
    [InlineData(HttpStatusCode.OK, "{}", AuthenticationFailureReason.Denied, $"The token endpoint {TokenEndpoint} returned no bearer token.")]
    public async Task Federated_UnusableAnswers_SayWhy(HttpStatusCode status, string body, AuthenticationFailureReason reason, string message)
    {
        UseFederatedFile();
        _http.Respond(status, body);
        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(reason);
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
        exception.Message.ShouldBe($"The token request to {TokenEndpoint} failed: No such host is known.");
    }

    [Theory]
    [InlineData(StorytellerVariables.TOKEN_FILE, null, "does not exist")]
    [InlineData(StorytellerVariables.TOKEN_FILE, "  \n", "is empty")]
    [InlineData(StorytellerVariables.SUBJECT_TOKEN_FILE, null, "does not exist")]
    public async Task Federated_MissingOrEmptyFile_IsAConfigurationError(string fileVariable, string? content, string problem)
    {
        var path = P(@"C:\ci\platform-token");
        _environment[StorytellerVariables.CLIENT_ID] = MachineId;
        _environment[fileVariable] = path;

        if (content is not null)
        {
            _fileSystem.AddFile(path, new MockFileData(content));
        }

        var service = CreateService();
        service.BindTarget("acme", "billing");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.GetAccessTokenAsync());

        exception.Reason.ShouldBe(AuthenticationFailureReason.Configuration);
        exception.Message.ShouldBe($"The platform token file {path} in {fileVariable} {problem}.");
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

    // Change against today's sform: client credentials are renewed after a 401 too, not only a federated token.
    [Fact]
    public async Task Renew_IsForEveryCredentialThatCanRenew()
    {
        _environment[StorytellerVariables.API_KEY] = TestApiKeys.Create("acme", "billing", "3f2c");
        var apiKey = CreateService();
        apiKey.BindTarget("acme", "billing");
        (await apiKey.RenewAccessTokenAsync((await apiKey.GetAccessTokenAsync())!)).ShouldBeNull();

        _environment.Remove(StorytellerVariables.API_KEY);
        var path = P(@"C:\ci\token");
        _fileSystem.AddFile(path, new MockFileData("file-token"));
        _environment[StorytellerVariables.TOKEN_FILE] = path;
        var tokenFile = CreateService();
        (await tokenFile.RenewAccessTokenAsync("file-token")).ShouldBeNull();

        _environment.Remove(StorytellerVariables.TOKEN_FILE);
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""")
            .Respond(HttpStatusCode.OK, """{"access_token":"cc-2","expires_in":3600}""");
        var clientCredentials = CreateService();
        (await clientCredentials.GetAccessTokenAsync()).ShouldBe("cc-1");
        (await clientCredentials.RenewAccessTokenAsync("cc-1")).ShouldBe("cc-2");
        (await clientCredentials.GetAccessTokenAsync()).ShouldBe("cc-2");

        _http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Renew_ThatIsRefused_IsDenied()
    {
        UseClientCredentials("client_01");
        _http.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""")
            .Respond(HttpStatusCode.BadRequest, """{"error":"invalid_client"}""");
        var service = CreateService();
        (await service.GetAccessTokenAsync()).ShouldBe("cc-1");

        var exception = await Should.ThrowAsync<AuthenticationException>(() => service.RenewAccessTokenAsync("cc-1"));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Denied);
        exception.Message.ShouldBe($"The token request to {TokenUrl} was rejected with 400: invalid_client.");
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

    private string AddCertificate(string spiffeUri, string fileVariable = StorytellerVariables.CLIENT_CERTIFICATE)
    {
        var path = P(@"C:\ci\machine.pfx");
        _fileSystem.AddFile(path, new MockFileData(TestCertificates.Pkcs12(spiffeUri)));
        _environment[fileVariable] = path;
        _environment[fileVariable == StorytellerVariables.CLIENT_CERTIFICATE ? StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD : StorytellerVariables.CERTIFICATE_PASSWORD] = TestCertificates.Password;
        return path;
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

    private string UseFederatedFile(string content = "platform-token", string fileVariable = StorytellerVariables.TOKEN_FILE)
    {
        var path = P(@"C:\ci\platform-token");
        _fileSystem.AddFile(path, new MockFileData(content));
        _environment[StorytellerVariables.CLIENT_ID] = MachineId;
        _environment[fileVariable] = path;
        return path;
    }

    // The variables of a GitHub Actions job with permissions: id-token: write.
    private void UseFederatedGitHub()
    {
        _environment[StorytellerVariables.CLIENT_ID] = MachineId;
        _environment[StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL] = GitHubRequestUrl;
        _environment[StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN] = GitHubRequestToken;
    }

    private MachineAuthenticationService CreateService(HttpMessageHandler? handler = null, StorytellerResilienceOptions? resilience = null)
    {
        return TestMachines.Create(TestMachines.Settings(_environment), _fileSystem, TestMachines.Hooks(handler ?? _http, resilience), _time);
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
