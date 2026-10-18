using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class MachineTokenRenewalHandlerTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string AnnotationsUrl = "https://storyteller.example.com/api/v1/acme/billing/default/annotations";

    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    // The API behind the handler, and the token endpoint of the machine's own client.
    private readonly ScriptedHttpHandler _api = new();
    private readonly ScriptedHttpHandler _tokens = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly Dictionary<string, string> _environment = new() { [StorytellerVariables.API_URL] = ApiUrl };

    [Fact]
    public async Task Unauthorized_IsSentOnceMoreWithANewToken_AndItsResponseDisposed()
    {
        var machine = CreateFederatedMachine();
        _tokens.Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.OK, "[]");
        var recorder = new ResponseRecorder(_api);

        using var response = await SendAsync(machine, recorder, await BearerRequestAsync(machine));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Select(request => request.Authorization).ShouldBe(["Bearer st-1", "Bearer st-2"]);
        _api.Requests.ShouldAllBe(request => request.Uri == new Uri(AnnotationsUrl));
        _tokens.Requests.Count.ShouldBe(2);
        IsDisposed(recorder.Responses[0]).ShouldBeTrue();
        IsDisposed(recorder.Responses[1]).ShouldBeFalse();
    }

    [Fact]
    public async Task SecondUnauthorized_IsReturned_WithoutAThirdRequest()
    {
        var machine = CreateFederatedMachine();
        _tokens.Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.Unauthorized, string.Empty);
        var recorder = new ResponseRecorder(_api);

        using var response = await SendAsync(machine, recorder, await BearerRequestAsync(machine));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.ShouldBeSameAs(recorder.Responses[1]);
        _api.Requests.Count.ShouldBe(2);
        _tokens.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Forbidden_IsNotRenewed()
    {
        var machine = CreateFederatedMachine();
        _api.Respond(HttpStatusCode.Forbidden, string.Empty);

        using var response = await SendAsync(machine, _api, await BearerRequestAsync(machine));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _api.Requests.ShouldHaveSingleItem();
        _tokens.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task NonFederatedMachine_GetsTheUnauthorized()
    {
        _environment[StorytellerVariables.CLIENT_ID] = "client_01";
        _environment[StorytellerVariables.CLIENT_SECRET] = "client-secret";
        _environment[StorytellerVariables.TOKEN_URL] = "https://auth.example.com/oauth2/token";
        _tokens.Respond(HttpStatusCode.OK, """{"access_token":"cc-1","expires_in":3600}""");
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty);
        var machine = CreateMachine();
        machine.BindTarget("acme", "billing");

        using var response = await SendAsync(machine, _api, await BearerRequestAsync(machine));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.ShouldHaveSingleItem().Authorization.ShouldBe("Bearer cc-1");
        _tokens.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ApiKeyScheme_IsNotRenewed()
    {
        var machine = CreateFederatedMachine(exchange: false);
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty);
        var request = new HttpRequestMessage(HttpMethod.Get, AnnotationsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", "2s.a.b");

        using var response = await SendAsync(machine, _api, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.ShouldHaveSingleItem();
        _tokens.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task StreamContent_GetsTheOriginalUnauthorized()
    {
        // An import: the stream belongs to the caller and may not rewind.
        var machine = CreateFederatedMachine();
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty);
        var request = await BearerRequestAsync(machine, HttpMethod.Post);
        request.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("zip")));

        using var response = await SendAsync(machine, _api, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _api.Requests.ShouldHaveSingleItem();
        _tokens.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task StringContent_IsSentOnceMore()
    {
        var machine = CreateFederatedMachine();
        _tokens.Respond(HttpStatusCode.OK, ExchangeAnswer("st-2"));
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.OK, "{}");
        var request = await BearerRequestAsync(machine, HttpMethod.Post);
        request.Content = new StringContent("""{"annotations":[]}""", Encoding.UTF8, "application/json");

        using var response = await SendAsync(machine, _api, request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _api.Requests.Select(sent => sent.Authorization).ShouldBe(["Bearer st-1", "Bearer st-2"]);

        // The same message goes again, so the POST keeps its body.
        _api.Requests.Select(sent => sent.Body).ShouldBe(["""{"annotations":[]}""", """{"annotations":[]}"""]);
    }

    [Fact]
    public async Task UnauthorizedAfterARedirectToAnotherHost_IsNotRenewed()
    {
        var machine = CreateFederatedMachine();
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty);

        using var response = await SendAsync(machine, new RedirectingHandler(_api, new Uri("https://other.example.net/annotations")), await BearerRequestAsync(machine));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var sent = _api.Requests.ShouldHaveSingleItem();
        sent.Uri.Host.ShouldBe("other.example.net");
        sent.Authorization.ShouldBeNull();
        _tokens.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RenewalThatFails_Propagates_AndDisposesTheUnauthorized()
    {
        var machine = CreateFederatedMachine();
        _tokens.Respond(HttpStatusCode.BadRequest, """{"error":"invalid_request","error_description":"The subject token was not accepted."}""");
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty);
        var recorder = new ResponseRecorder(_api);
        var request = await BearerRequestAsync(machine);

        var exception = await Should.ThrowAsync<AuthenticationException>(() => SendAsync(machine, recorder, request));

        exception.Reason.ShouldBe(AuthenticationFailureReason.Denied);
        exception.Message.ShouldNotContain("platform-token");
        IsDisposed(recorder.Responses.ShouldHaveSingleItem()).ShouldBeTrue();
        _api.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Renewal_IsNeverCancelledByTheRequest()
    {
        // The token factory passes none either; the timeout of the exchange client ends a hanging exchange.
        var machine = new Mock<IMachineSignIn>();
        machine.SetupGet(signIn => signIn.IsConfigured).Returns(true);
        machine.Setup(signIn => signIn.RenewAccessTokenAsync("st-1", It.IsAny<CancellationToken>())).ReturnsAsync("st-2");
        _api.Respond(HttpStatusCode.Unauthorized, string.Empty)
            .Respond(HttpStatusCode.OK, "[]");
        var request = new HttpRequestMessage(HttpMethod.Get, AnnotationsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "st-1");
        using var cancellation = new CancellationTokenSource();

        using var response = await SendAsync(machine.Object, _api, request, cancellation.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        machine.Verify(signIn => signIn.RenewAccessTokenAsync("st-1", CancellationToken.None), Times.Once);
    }

    private static string ExchangeAnswer(string accessToken)
    {
        return $$"""{"access_token":"{{accessToken}}","token_type":"Bearer","expires_in":900}""";
    }

    // A disposed response has disposed its content, which then cannot be read.
    private static bool IsDisposed(HttpResponseMessage response)
    {
        try
        {
            response.Content.ReadAsStream();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        IMachineSignIn machine,
        HttpMessageHandler api,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        using var invoker = new HttpMessageInvoker(new MachineTokenRenewalHandler(machine) { InnerHandler = api }, disposeHandler: false);
        return await invoker.SendAsync(request, cancellationToken);
    }

    // A request as the SDK sends it: with the token the machine has now.
    private static async Task<HttpRequestMessage> BearerRequestAsync(MachineAuthenticationService machine, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, AnnotationsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await machine.GetAccessTokenAsync());
        return request;
    }

    // Bound to acme.billing; unless exchange is false, it holds st-1 already.
    private MachineAuthenticationService CreateFederatedMachine(bool exchange = true)
    {
        var path = MockUnixSupport.Path(@"C:\ci\platform-token");
        _fileSystem.AddFile(path, new MockFileData("platform-token"));
        _environment[StorytellerVariables.CLIENT_ID] = "6d0f2c4e-0000-4000-8000-000000000001";
        _environment[StorytellerVariables.SUBJECT_TOKEN_FILE] = path;

        if (exchange)
        {
            _tokens.Respond(HttpStatusCode.OK, ExchangeAnswer("st-1"));
        }

        var machine = CreateMachine();
        machine.BindTarget("acme", "billing");
        return machine;
    }

    private MachineAuthenticationService CreateMachine()
    {
        var settings = MachineSignInSettings.FromEnvironment(name => _environment.GetValueOrDefault(name));
        return new MachineAuthenticationService(
            settings,
            _fileSystem,
            new StubHttpClientFactory(_tokens),
            Mock.Of<IAuthenticationConfigurationResolver>(),
            new ManualTimeProvider(Start));
    }

    // Keeps the responses of the API, so a test can tell which ones the handler disposed.
    private sealed class ResponseRecorder : DelegatingHandler
    {
        public ResponseRecorder(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public List<HttpResponseMessage> Responses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            Responses.Add(response);
            return response;
        }
    }

    // What the redirect handler of HttpClientHandler does on a 302: the same message gets the new URI and loses
    // its Authorization header before it goes to the other host.
    private sealed class RedirectingHandler : DelegatingHandler
    {
        private readonly Uri _location;

        public RedirectingHandler(HttpMessageHandler innerHandler, Uri location)
            : base(innerHandler)
        {
            _location = location;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = _location;
            request.Headers.Authorization = null;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
