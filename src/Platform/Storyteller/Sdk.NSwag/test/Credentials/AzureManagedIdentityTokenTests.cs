using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Time.Testing;
using Polly.CircuitBreaker;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

// A federated machine on an Azure host: the platform token is the token of the host's managed identity for the resource
// of the Storyteller app registration, requested before every exchange from the endpoint the host names, or from IMDS.
public class AzureManagedIdentityTokenTests
{
    private const string MachineId = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
    private const string Resource = "api://4f6a1c2e-7b3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string EscapedResource = "api%3A%2F%2F4f6a1c2e-7b3d-4e5f-8a9b-0c1d2e3f4a5b";
    private const string UserAssignedClientId = "7c4d2b1a-0e9f-4a3b-8c7d-6e5f4a3b2c1d";
    private const string IdentityEndpoint = "http://127.0.0.1:41741/MSI/token/";
    private const string IdentityHeader = "1dent1ty-h3ader-s3cret";
    private const string Imds = "http://169.254.169.254/metadata/identity/oauth2/token";
    private const string TokenEndpoint = "https://api.example.com/api/v1/acme/billing/access/token";

    private const string Hint =
        " Does the host have a managed identity, and is the resource the identifier URI of the Storyteller app registration (api://<appId>)?";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero));
    private readonly StubHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly Dictionary<string, string?> _variables = new()
    {
        ["STORYTELLER_CLIENT_ID"] = MachineId,
        ["STORYTELLER_MANAGED_IDENTITY_RESOURCE"] = Resource,
    };

    [Fact]
    public async Task OnAppService_TheIdentityEndpoint_IsAskedForATokenOfTheResource()
    {
        UseAppService();
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));

        var authorization = await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None);

        authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "storyteller-1"));
        _http.Requests.Count.ShouldBe(2);
        var identity = _http.Requests[0];
        identity.Method.ShouldBe(HttpMethod.Get);
        identity.Uri.OriginalString.ShouldBe($"{IdentityEndpoint}?api-version=2019-08-01&resource={EscapedResource}");
        identity.Headers["X-IDENTITY-HEADER"].ShouldBe(IdentityHeader);
        identity.Headers["Accept"].ShouldBe("application/json");
        identity.Headers.ContainsKey("Metadata").ShouldBeFalse();
        identity.Authorization.ShouldBeNull();
        var exchange = _http.Requests[1];
        exchange.Method.ShouldBe(HttpMethod.Post);
        exchange.Uri.ShouldBe(new Uri(TokenEndpoint));
        exchange.Form["subject_token"].ShouldBe("mi-token-1");
        exchange.Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:access_token");
        exchange.Form["client_id"].ShouldBe(MachineId);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, IdentityHeader)]
    [InlineData(IdentityEndpoint, null)]
    [InlineData(" ", IdentityHeader)]
    [InlineData(IdentityEndpoint, " ")]
    public async Task WithoutTheEndpointAndItsHeader_ImdsIsAsked(string? identityEndpoint, string? identityHeader)
    {
        _variables["IDENTITY_ENDPOINT"] = identityEndpoint;
        _variables["IDENTITY_HEADER"] = identityHeader;
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));

        await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None);

        var identity = _http.Requests[0];
        identity.Method.ShouldBe(HttpMethod.Get);
        identity.Uri.OriginalString.ShouldBe($"{Imds}?api-version=2018-02-01&resource={EscapedResource}");
        identity.Headers["Metadata"].ShouldBe("true");
        identity.Headers.ContainsKey("X-IDENTITY-HEADER").ShouldBeFalse();
        identity.Authorization.ShouldBeNull();
        _http.Requests[1].Form["subject_token"].ShouldBe("mi-token-1");
    }

    [Theory]
    [InlineData(true, IdentityEndpoint + "?api-version=2019-08-01")]
    [InlineData(false, Imds + "?api-version=2018-02-01")]
    public async Task AUserAssignedIdentity_IsPickedByItsClientId(bool isAppService, string expectedStart)
    {
        if (isAppService)
        {
            UseAppService();
        }

        _variables["STORYTELLER_MANAGED_IDENTITY_CLIENT_ID"] = UserAssignedClientId;
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));

        await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests[0].Uri.OriginalString.ShouldBe($"{expectedStart}&resource={EscapedResource}&client_id={UserAssignedClientId}");
    }

    // The platform rotates IDENTITY_HEADER, so nothing of the host is kept between two exchanges.
    [Fact]
    public async Task TheHostVariables_AreReadBeforeEveryExchange_AndTheHeaderGoesToTheIdentityEndpointOnly()
    {
        UseAppService();
        _variables["STORYTELLER_TOKEN_SCOPE"] = "Configuration.Read";
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"))
            .Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-2"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-2"));
        var credential = Bind(Create());

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _variables["IDENTITY_ENDPOINT"] = "http://127.0.0.1:41999/MSI/token/";
        _variables["IDENTITY_HEADER"] = "r0tated-h3ader-s3cret";
        var renewed = await credential.GetAuthorizationAsync(first, CancellationToken.None);

        renewed!.Parameter.ShouldBe("storyteller-2");
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Post]);
        _http.Requests[0].Headers["X-IDENTITY-HEADER"].ShouldBe(IdentityHeader);
        _http.Requests[2].Uri.OriginalString.ShouldStartWith("http://127.0.0.1:41999/MSI/token/?api-version=2019-08-01");
        _http.Requests[2].Headers["X-IDENTITY-HEADER"].ShouldBe("r0tated-h3ader-s3cret");
        _http.Requests[3].Form["subject_token"].ShouldBe("mi-token-2");
        _http.Requests[3].Form["scope"].ShouldBe("Configuration.Read");
        var exchanges = _http.Requests.Where(request => request.Method == HttpMethod.Post).ToList();
        exchanges.ShouldAllBe(request => request.Authorization == null);
        exchanges.ShouldAllBe(request => !request.Headers.ContainsKey("X-IDENTITY-HEADER"));
        exchanges.ShouldAllBe(request => !request.Headers.Values.Any(value => value.Contains("h3ader-s3cret")));
        exchanges.ShouldAllBe(request => !request.Body!.Contains("h3ader-s3cret"));
    }

    // The endpoint is on the host or its network, where a proxy cannot reach it, and over http a proxy would get the
    // header of the host. So a proxy of the application serves the token endpoint, and never this request. Over real
    // sockets: the seam of the other tests replaces the handler that has the proxy.
    [Fact]
    public async Task AProxyOfTheApplication_ServesTheTokenEndpoint_AndNeverTheManagedIdentityRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = new TcpListener(IPAddress.Loopback, 0);
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        host.Start();
        proxy.Start();
        var identityRequest = ServeOneAsync(host, IdentityAnswer("mi-token-1"), cancellationToken);
        var proxiedRequest = ServeOneAsync(proxy, TestTokens.TokenResponse("storyteller-1"), cancellationToken);
        UseAppService($"http://{host.LocalEndpoint}/msi/token");
        var options = FederatedTokenOptions.ForAzureManagedIdentity(MachineId, Resource) with
        {
            ConfigureHandler = handler =>
            {
                handler.Proxy = new WebProxy($"http://{proxy.LocalEndpoint}");
                handler.UseProxy = true;
            },
        };
        using var credential = FederatedTokenCredential.Create(options, null, name => _variables.GetValueOrDefault(name))
            .Bind(new StorytellerCredentialBinding(new Uri("http://localhost:7071/api"))
            {
                Organization = "acme",
                Project = "billing",
                TimeProvider = _time,
                Resilience = new StorytellerResilienceOptions { MaxRetryAttempts = 0 },
            });

        var authorization = await credential.GetAuthorizationAsync(null, cancellationToken);

        authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "storyteller-1"));
        var identity = await identityRequest;
        identity.ShouldStartWith($"GET /msi/token?api-version=2019-08-01&resource={EscapedResource} HTTP/1.1\r\n");
        identity.ShouldContain($"X-IDENTITY-HEADER: {IdentityHeader}\r\n");
        var proxied = await proxiedRequest;
        proxied.ShouldStartWith("POST http://localhost:7071/api/v1/acme/billing/access/token HTTP/1.1\r\n");
        proxied.ShouldNotContain(IdentityHeader);
    }

    // An Azure Function needs only its app settings; the host adds its own two variables.
    [Fact]
    public async Task AnUnboundCredential_ExchangesAtTheTokenEndpointOfTheEnvironment()
    {
        UseAppService();
        _variables["STORYTELLER_ENDPOINT"] = "http://localhost:7071/api";
        _variables["STORYTELLER_KEY"] = "@contoso.payments.default.rst.invoicing";
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("  mi-token-1  "))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var credential = Create().WithPrimaryHandler(_http);

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        var cached = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        first.ShouldBe(new AuthenticationHeaderValue("Bearer", "storyteller-1"));
        cached.ShouldBe(first);
        credential.CanRenew.ShouldBeTrue();
        _http.Requests.Count.ShouldBe(2);
        _http.Requests[0].Uri.OriginalString.ShouldBe($"{IdentityEndpoint}?api-version=2019-08-01&resource={EscapedResource}");
        var exchange = _http.Requests[1];
        exchange.Uri.ShouldBe(new Uri("http://localhost:7071/api/v1/contoso/payments/access/token"));
        exchange.Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["client_id"] = MachineId,
            ["subject_token"] = "mi-token-1",
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
        });
    }

    [Fact]
    public async Task TheOptionsOfCode_AskTheSameHost()
    {
        UseAppService();
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var options = FederatedTokenOptions.ForAzureManagedIdentity(MachineId, Resource, UserAssignedClientId);
        var credential = FederatedTokenCredential.Create(options, null, name => _variables.GetValueOrDefault(name));

        await Bind(credential).GetAuthorizationAsync(null, CancellationToken.None);

        options.SubjectTokenType.ShouldBe(FederatedTokenOptions.AccessTokenType);
        credential.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.AzureManagedIdentity);
        _http.Requests[0].Uri.OriginalString.ShouldBe($"{IdentityEndpoint}?api-version=2019-08-01&resource={EscapedResource}&client_id={UserAssignedClientId}");
        _http.Requests[1].Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:access_token");
    }

    // access_token is the default of the variables and of ForAzureManagedIdentity only: SubjectTokenType has one default
    // for every source, so options that set AzureManagedIdentity themselves set the type too.
    [Theory]
    [InlineData(null, "urn:ietf:params:oauth:token-type:jwt")]
    [InlineData("access_token", "urn:ietf:params:oauth:token-type:access_token")]
    public async Task OptionsThatSetTheManagedIdentityThemselves_KeepTheDefaultTokenType_UnlessTheySetIt(string? subjectTokenType, string expected)
    {
        UseAppService();
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var options = new FederatedTokenOptions { ClientId = MachineId, AzureManagedIdentity = new AzureManagedIdentityTokenOptions { Resource = Resource } };
        options = subjectTokenType is null ? options : options with { SubjectTokenType = subjectTokenType };
        var credential = FederatedTokenCredential.Create(options, null, name => _variables.GetValueOrDefault(name));

        await Bind(credential).GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests[0].Uri.OriginalString.ShouldBe($"{IdentityEndpoint}?api-version=2019-08-01&resource={EscapedResource}");
        _http.Requests[1].Form["subject_token_type"].ShouldBe(expected);
    }

    // What the platform sets: 127.0.0.1 on Windows App Service, localhost on Container Apps, a link-local address on
    // Linux App Service; and the other addresses of a host's own network, and https.
    [Theory]
    [InlineData("http://127.0.0.1:41741/MSI/token/")]
    [InlineData("http://localhost:42356/msi/token")]
    [InlineData("http://LOCALHOST:42356/msi/token")]
    [InlineData("http://[::1]:42356/msi/token")]
    [InlineData("http://169.254.129.5:8081/msi/token")]
    [InlineData("http://[fe80::1]:8081/msi/token")]
    [InlineData("http://10.0.0.4:8081/msi/token")]
    [InlineData("http://172.16.0.1/msi/token")]
    [InlineData("http://172.31.255.254/msi/token")]
    [InlineData("http://192.168.1.10/msi/token")]
    [InlineData("http://[fd00::1]/msi/token")]
    [InlineData("http://[::ffff:10.0.0.4]/msi/token")]
    [InlineData("https://identity.example.com/msi/token")]
    public async Task AnIdentityEndpoint_OfTheHostsNetwork_OrOverHttps_IsAsked(string identityEndpoint)
    {
        UseAppService(identityEndpoint);
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));

        await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests[0].Uri.OriginalString.ShouldBe($"{identityEndpoint}?api-version=2019-08-01&resource={EscapedResource}");
        _http.Requests[0].Headers["X-IDENTITY-HEADER"].ShouldBe(IdentityHeader);
    }

    // The header is a secret of the host: never over http to a name that DNS resolves, or to a public address.
    [Theory]
    [InlineData("http://identity.example.com/msi/token")]
    [InlineData("http://loopback/msi/token")]
    [InlineData("http://host.docker.internal:8081/msi/token")]
    [InlineData("http://8.8.8.8/msi/token")]
    [InlineData("http://172.15.255.254/msi/token")]
    [InlineData("http://172.32.0.1/msi/token")]
    [InlineData("http://192.169.0.1/msi/token")]
    [InlineData("http://169.255.0.1/msi/token")]
    [InlineData("http://[2001:db8::1]/msi/token")]
    [InlineData("http://user@127.0.0.1:41741/MSI/token/")]
    [InlineData("https://user:p4ssw0rd@identity.example.com/msi/token")]
    [InlineData("ftp://127.0.0.1/msi/token")]
    [InlineData("/msi/token")]
    [InlineData("not a url")]
    public async Task AnIdentityEndpointThatBreaksItsRule_IsAConfigurationError_ThatNamesTheVariableButNotTheValue(string identityEndpoint)
    {
        UseAppService(identityEndpoint);

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Configuration);

        exception.Message.ShouldBe(
            "IDENTITY_ENDPOINT must be an absolute URL without user info: https, or http to localhost or to a loopback, link-local or private address.");
        _http.Requests.ShouldBeEmpty();
    }

    // The public rule of every other URL stays as strict as it was.
    [Theory]
    [InlineData("http://169.254.129.5:8081/msi/token")]
    [InlineData("http://10.0.0.4:8081/msi/token")]
    [InlineData("http://[fd00::1]/msi/token")]
    public void TheRuleOfTheIdentityEndpoint_IsWiderThanTheUrlRule(string identityEndpoint)
    {
        var uri = new Uri(identityEndpoint);

        AzureManagedIdentityEndpoint.IsAllowed(uri).ShouldBeTrue();
        StorytellerUrlRule.IsAllowed(uri).ShouldBeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ABusyIdentityEndpoint_IsUnavailable_AfterRetries(HttpStatusCode status)
    {
        UseAppService();
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(status, """{ "message": "Try again later." }"""));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Unavailable);

        exception.Message.ShouldBe($"The managed identity token request to {IdentityEndpoint} for the resource {Resource} failed with {(int)status}: Try again later.{Hint}");
        exception.InnerException.ShouldBeOfType<StorytellerAuthenticationException>().Reason.ShouldBe(StorytellerAuthenticationFailureReason.Unavailable);
        _http.Requests.Count.ShouldBe(4);
        _http.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get);
    }

    // The hint is a sentence of its own: the runtime's text of a refused connection ends with the address, not a period.
    [Theory]
    [InlineData(false, "A socket operation was attempted to an unreachable network.", "A socket operation was attempted to an unreachable network.")]
    [InlineData(false, "Connection refused (169.254.169.254:80)", "Connection refused (169.254.169.254:80).")]
    [InlineData(true, "The request timed out.", "The request timed out.")]
    public async Task AnUnreachableIdentityEndpoint_IsUnavailable(bool isTimeout, string failure, string sentence)
    {
        _http.Otherwise = (_, _) => isTimeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException(failure))
            : Task.FromException<HttpResponseMessage>(new HttpRequestException(failure));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Unavailable);

        exception.Message.ShouldBe($"The managed identity token request to {Imds} for the resource {Resource} failed: {sentence}{Hint}");
        _http.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get);
    }

    // One circuit breaker serves the host and the token endpoint. When the token endpoint opened it, the failure names
    // that as the last one, and nothing asks about the host, which answered.
    [Fact]
    public async Task AnOpenCircuit_NamesTheLastFailure_AndDoesNotAskAboutTheHost()
    {
        UseAppService();
        _http.Otherwise = (request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? StubHttpHandler.Response(HttpStatusCode.OK, IdentityAnswer("mi-token-1"))
            : StubHttpHandler.Response(HttpStatusCode.ServiceUnavailable));
        var credential = Bind(Create());

        for (var call = 0; call < 2; call++)
        {
            var refused = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

            refused.Message.ShouldBe($"The token request to {TokenEndpoint} failed with 503.");
        }

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Unavailable);
        exception.InnerException.ShouldBeOfType<BrokenCircuitException>();
        exception.Message.ShouldStartWith($"The managed identity token request to {IdentityEndpoint} for the resource {Resource} failed: ");
        exception.Message.ShouldEndWith($" The last failure: The token request to {TokenEndpoint} failed with 503.");
        exception.Message.ShouldNotContain("Does the host have a managed identity");

        // Two calls of one answer of the host and four attempts of the exchange; the third call reaches nothing.
        _http.Requests.Count.ShouldBe(10);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "invalid_resource", "error_description": "AADSTS500011: The resource principal was not found." }""", "was rejected with 400: invalid_resource: AADSTS500011: The resource principal was not found.")]
    [InlineData(HttpStatusCode.BadRequest, """{ "statusCode": 400, "message": "Unable to load requested managed identity." }""", "was rejected with 400: Unable to load requested managed identity.")]
    [InlineData(HttpStatusCode.Unauthorized, "", "was rejected with 401.")]
    [InlineData(HttpStatusCode.Forbidden, "", "was rejected with 403.")]
    [InlineData(HttpStatusCode.NotFound, "", "was rejected with 404.")]
    [InlineData(HttpStatusCode.Gone, "", "was rejected with 410.")]
    [InlineData(HttpStatusCode.Found, "", "was answered with a redirect (302), which is not followed.")]
    [InlineData(HttpStatusCode.OK, """{ "token_type": "Bearer" }""", "returned no token.")]
    [InlineData(HttpStatusCode.OK, """{ "access_token": " " }""", "returned no token.")]
    [InlineData(HttpStatusCode.OK, "<html/>", "returned no token.")]
    public async Task ARefusingIdentityEndpoint_IsRejected_WithoutARetry(HttpStatusCode status, string body, string ending)
    {
        UseAppService();
        _http.Respond(status, body.Length > 0 ? body : null, ("Location", "https://elsewhere.example.com/msi/token"));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Rejected);

        exception.Message.ShouldBe($"The managed identity token request to {IdentityEndpoint} for the resource {Resource} {ending}{Hint}");
        _http.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    // IMDS answers 404 and 410 while it is updating; a host without a managed identity answers 400.
    [Theory]
    [InlineData(HttpStatusCode.NotFound, StorytellerAuthenticationFailureReason.Unavailable, "failed with 404.")]
    [InlineData(HttpStatusCode.Gone, StorytellerAuthenticationFailureReason.Unavailable, "failed with 410.")]
    [InlineData(HttpStatusCode.BadRequest, StorytellerAuthenticationFailureReason.Rejected, "was rejected with 400.")]
    [InlineData(HttpStatusCode.Found, StorytellerAuthenticationFailureReason.Rejected, "was answered with a redirect (302), which is not followed.")]
    public async Task ImdsThatIsUpdating_IsUnavailable_AndAHostWithoutAnIdentityIsRejected(HttpStatusCode status, StorytellerAuthenticationFailureReason reason, string ending)
    {
        _http.Respond(status, null, ("Location", "https://elsewhere.example.com/metadata/identity/oauth2/token"));

        var exception = await ShouldFail(reason);

        exception.Message.ShouldBe($"The managed identity token request to {Imds} for the resource {Resource} {ending}{Hint}");
        _http.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    // The server's refusal is the token endpoint's; the hint of a GitHub Actions job does not belong here.
    [Fact]
    public async Task ARefusedExchange_IsTheTokenEndpointsOwnMessage()
    {
        UseAppService();
        _http.Respond(HttpStatusCode.OK, IdentityAnswer("mi-token-s3cret"))
            .Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_request", "error_description": "The subject token was not accepted." }""");

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Rejected);

        exception.Message.ShouldBe($"The token request to {TokenEndpoint} was rejected with 400: invalid_request: The subject token was not accepted.");
        exception.Message.ShouldNotContain("mi-token-s3cret");
    }

    // Beyond the two protocols: such a host gets its token through a SubjectTokenProvider.
    [Theory]
    [InlineData("IDENTITY_SERVER_THUMBPRINT", IdentityHeader, "Service Fabric (IDENTITY_SERVER_THUMBPRINT is set)")]
    [InlineData("IMDS_ENDPOINT", null, "Azure Arc (IMDS_ENDPOINT is set with IDENTITY_ENDPOINT and without IDENTITY_HEADER)")]
    public async Task AHostTheSourceDoesNotCover_IsAConfigurationError_ThatNamesTheProvider(string variable, string? identityHeader, string host)
    {
        UseAppService("http://localhost:40342/metadata/identity/oauth2/token");
        _variables["IDENTITY_HEADER"] = identityHeader;
        _variables[variable] = "http://localhost:40342";

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Configuration);

        exception.Message.ShouldBe(
            $"The built-in managed identity source covers App Service, Functions, Container Apps and IMDS, not {host}; get the platform token in code with FederatedTokenOptions.SubjectTokenProvider, for example with Azure.Identity.");
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserAssignedClientId)]
    public void TheCredential_PrintsItsSourceItsResourceAndItsIdentity_AndNothingOfTheHost(string? managedIdentityClientId)
    {
        UseAppService();
        _variables["STORYTELLER_MANAGED_IDENTITY_CLIENT_ID"] = managedIdentityClientId;
        var credential = Create().ShouldBeOfType<FederatedTokenCredential>();

        string[] texts = [credential.ToString(), Bind(credential).ToString()!, AzureManagedIdentityEndpoint.Read(name => _variables.GetValueOrDefault(name)).ToString()];

        credential.SubjectTokenSource.ShouldBe(StorytellerSubjectTokenSource.AzureManagedIdentity);
        texts[0].ShouldBe($"StorytellerCredential {{ Kind = Federated, ClientId = {MachineId}, SubjectTokenSource = AzureManagedIdentity, Resource = {Resource}, ManagedIdentityClientId = {managedIdentityClientId} }}");
        texts[1].ShouldBe(texts[0]);
        texts[2].ShouldBe($"AzureManagedIdentityEndpoint {{ Url = {IdentityEndpoint}, IsImds = False }}");
        texts.ShouldAllBe(text => !text.Contains(IdentityHeader));
    }

    private static string IdentityAnswer(string accessToken)
    {
        return $$"""{"access_token":"{{accessToken}}","expires_on":"1794816000","resource":"{{Resource}}","token_type":"Bearer","client_id":"{{UserAssignedClientId}}"}""";
    }

    // Answers one HTTP/1.1 request with the JSON and returns the head of the request, as it came over the socket.
    private static async Task<string> ServeOneAsync(TcpListener listener, string json, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var request = new StringBuilder();
        var buffer = new byte[4096];
        var headLength = -1;
        var bodyLength = 0;

        // The body is read too: closing a socket with unread data resets the connection under the answer.
        while (headLength < 0 || request.Length < headLength + bodyLength)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                break;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));

            if (headLength < 0 && request.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) is >= 0 and var end)
            {
                headLength = end + 4;
                var contentLength = Regex.Match(request.ToString(0, headLength), @"\r\nContent-Length: (\d+)\r\n", RegexOptions.IgnoreCase);
                bodyLength = contentLength.Success ? int.Parse(contentLength.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            }
        }

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {json.Length}\r\nConnection: close\r\n\r\n{json}"),
            cancellationToken);
        return request.ToString(0, Math.Max(headLength, 0));
    }

    // The variables of App Service, Functions and Container Apps on a host with a managed identity.
    private void UseAppService(string identityEndpoint = IdentityEndpoint)
    {
        _variables["IDENTITY_ENDPOINT"] = identityEndpoint;
        _variables["IDENTITY_HEADER"] = IdentityHeader;
    }

    private StorytellerCredential Create()
    {
        return StorytellerCredential.FromEnvironment(name => _variables.GetValueOrDefault(name), _fileSystem, _time).ShouldNotBeNull();
    }

    private StorytellerCredential Bind(StorytellerCredential credential)
    {
        return credential
            .Bind(new StorytellerCredentialBinding(new Uri("https://api.example.com/api/"))
            {
                Organization = "acme",
                Project = "billing",
                TimeProvider = _time,
                Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero },
            })
            .WithPrimaryHandler(_http);
    }

    // No message, of the failure or of the failures under it, holds the header or the query of the request.
    private async Task<StorytellerAuthenticationException> ShouldFail(StorytellerAuthenticationFailureReason reason)
    {
        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(reason);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            current.Message.ShouldNotContain(IdentityHeader);
            current.Message.ShouldNotContain("api-version");
            current.Message.ShouldNotContain("p4ssw0rd");
        }

        return exception;
    }
}
