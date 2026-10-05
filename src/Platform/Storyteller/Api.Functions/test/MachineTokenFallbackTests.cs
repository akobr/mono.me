using System.Net;
using System.Security.Claims;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The machine provider may differ from the user provider: BearerAuthenticationMiddleware asks the
// machine validators only after the user validator rejected the token.
public class MachineTokenFallbackTests
{
    private const string MachineIssuer = "https://keycloak.example/realms/storyteller";

    private readonly RecordingMiddleware _middleware = new();

    [Fact]
    public async Task UserValidatorRejects_MachineValidatorForTheIssuerAccepts()
    {
        await WithValidationAsync(async () =>
        {
            var machine = new RecordingMachineTokenValidator(MachineIssuer) { Result = MachineResult() };
            var context = CreateContext(new RecordingBearerTokenValidator(), machine);

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            machine.Calls.ShouldBe(1);
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("machine-client");
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.ClientCredentials);
        });
    }

    [Fact]
    public async Task UserValidatorAccepts_MachineValidatorIsNotAsked()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult([new Claim("sub", "user-1")], false, null) };
            var machine = new RecordingMachineTokenValidator(MachineIssuer) { Result = MachineResult() };
            var context = CreateContext(user, machine);

            await InvokeAsync(context);

            machine.Calls.ShouldBe(0);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task IssuerOfNoMachineValidator_IsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            var machine = new RecordingMachineTokenValidator("https://other.example") { Result = MachineResult() };
            var context = CreateContext(new RecordingBearerTokenValidator(), machine);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            machine.Calls.ShouldBe(0);
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task UserResultFromAMachineValidator_IsIgnored()
    {
        await WithValidationAsync(async () =>
        {
            var machine = new RecordingMachineTokenValidator(MachineIssuer)
            {
                Result = new BearerValidationResult([new Claim("sub", "user-1")], false, null),
            };
            var context = CreateContext(new RecordingBearerTokenValidator(), machine);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task MachineKeyFailure_IsServiceUnavailable()
    {
        await WithValidationAsync(async () =>
        {
            var machine = new RecordingMachineTokenValidator(MachineIssuer)
            {
                Error = new BearerKeyRetrievalException("down", new HttpRequestException("down")),
            };
            var context = CreateContext(new RecordingBearerTokenValidator(), machine);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        });
    }

    private static BearerValidationResult MachineResult()
    {
        return new BearerValidationResult([new Claim("azp", "machine-client"), new Claim("scp", "Annotation.Read")], true, "machine-client");
    }

    private static TestFunctionContext CreateContext(RecordingBearerTokenValidator user, RecordingMachineTokenValidator machine)
    {
        var services = FunctionTestDoubles.CreateServices(user, null, machine);
        var token = UnsignedJwt($$"""{"iss":"{{MachineIssuer}}","azp":"machine-client"}""");
        var (context, _) = FunctionTestDoubles.CreateRequest(services, new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        return context;
    }

    private static async Task WithValidationAsync(Func<Task> body)
    {
        var previous = BearerValidationMode.DecodeWithoutValidation;
        BearerValidationMode.DecodeWithoutValidation = false;

        try
        {
            await body();
        }
        finally
        {
            BearerValidationMode.DecodeWithoutValidation = previous;
        }
    }

    private static string UnsignedJwt(string payloadJson)
    {
        return string.Join('.', Base64Url("""{"alg":"none","typ":"JWT"}"""), Base64Url(payloadJson), "x");
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<bool> InvokeAsync(TestFunctionContext context)
    {
        var called = false;
        await _middleware.Invoke(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        return called;
    }

    private sealed class RecordingMiddleware : BearerAuthenticationMiddleware
    {
        public RecordingMiddleware()
            : base(NullLogger<BearerAuthenticationMiddleware>.Instance)
        {
        }

        public HttpResponseData? Response { get; private set; }

        protected override void AssignResponse(FunctionContext context, HttpResponseData response)
        {
            Response = response;
        }
    }
}
