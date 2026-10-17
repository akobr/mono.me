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
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public class MachineTokenFallbackTests
{
    private const string MachineIssuer = "https://keycloak.example/realms/storyteller";
    private const string StorytellerIssuer = "urn:storyteller:2s.platform";
    private const string FederatedMachineId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";

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

    [Fact]
    public async Task OwnedIssuer_SkipsTheUserValidator_AndStoresTheKindAndProject()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Error = new InvalidOperationException("should not be called") };
            var storyteller = new RecordingMachineTokenValidator(StorytellerIssuer) { Owns = true, Result = FederatedResult() };
            var context = CreateContext(user, StorytellerIssuer, storyteller);

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            user.Calls.ShouldBe(0);
            storyteller.Calls.ShouldBe(1);
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe(FederatedMachineId);
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.Federated);
            context.Items[FunctionContextItemKeys.MachineOrganization].ShouldBe("org1");
            context.Items[FunctionContextItemKeys.MachineProject].ShouldBe("proj1");
        });
    }

    [Fact]
    public async Task OwnedIssuer_RefusedByItsValidator_IsUnauthorizedWithoutAskingAnyOther()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = MachineResult() };
            var storyteller = new RecordingMachineTokenValidator(StorytellerIssuer) { Owns = true };
            var sameIssuer = new RecordingMachineTokenValidator(StorytellerIssuer) { Result = MachineResult() };
            var context = CreateContext(user, StorytellerIssuer, storyteller, sameIssuer);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            storyteller.Calls.ShouldBe(1);
            user.Calls.ShouldBe(0);
            sameIssuer.Calls.ShouldBe(0);
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task OwnedIssuer_UserResult_IsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            var storyteller = new RecordingMachineTokenValidator(StorytellerIssuer)
            {
                Owns = true,
                Result = new BearerValidationResult([new Claim("sub", "user-1")], false, null),
            };
            var context = CreateContext(new RecordingBearerTokenValidator(), StorytellerIssuer, storyteller);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Fact]
    public async Task OwnedIssuer_KeyFailure_IsServiceUnavailable()
    {
        await WithValidationAsync(async () =>
        {
            var storyteller = new RecordingMachineTokenValidator(StorytellerIssuer)
            {
                Owns = true,
                Error = new BearerKeyRetrievalException("no signing key"),
            };
            var context = CreateContext(new RecordingBearerTokenValidator(), StorytellerIssuer, storyteller);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OwnedIssuer_UnderDebugDecoding_IsStillValidated(bool accepted)
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var storyteller = new RecordingMachineTokenValidator(StorytellerIssuer)
            {
                Owns = true,
                Result = accepted ? FederatedResult() : null,
            };
            var context = CreateContext(new RecordingBearerTokenValidator(), StorytellerIssuer, storyteller);

            var called = await InvokeAsync(context);

            storyteller.Calls.ShouldBe(1);
            called.ShouldBe(accepted);

            if (accepted)
            {
                context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.Federated);
                context.Items[FunctionContextItemKeys.MachineProject].ShouldBe("proj1");
            }
            else
            {
                _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }
        });
    }

    [Fact]
    public async Task IssuerNobodyOwns_UnderDebugDecoding_IsDecoded()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var machine = new RecordingMachineTokenValidator(MachineIssuer) { Result = MachineResult() };
            var context = CreateContext(new RecordingBearerTokenValidator(), MachineIssuer, machine);

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            machine.Calls.ShouldBe(0);
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.ClientCredentials);
        });
    }

    private static BearerValidationResult FederatedResult()
    {
        return new BearerValidationResult(
            [new Claim("azp", FederatedMachineId), new Claim("sub", FederatedMachineId), new Claim("roles", "Default.Read")],
            true,
            FederatedMachineId,
            MachineCredentialKind.Federated,
            "org1",
            "proj1");
    }

    private static BearerValidationResult MachineResult()
    {
        return new BearerValidationResult([new Claim("azp", "machine-client"), new Claim("scp", "Annotation.Read")], true, "machine-client");
    }

    private static TestFunctionContext CreateContext(RecordingBearerTokenValidator user, RecordingMachineTokenValidator machine)
    {
        return CreateContext(user, MachineIssuer, machine);
    }

    private static TestFunctionContext CreateContext(RecordingBearerTokenValidator user, string issuer, params RecordingMachineTokenValidator[] machines)
    {
        var services = FunctionTestDoubles.CreateServices(user, null, machines);
        var token = UnsignedJwt($$"""{"iss":"{{issuer}}","azp":"machine-client"}""");
        var (context, _) = FunctionTestDoubles.CreateRequest(services, new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
        return context;
    }

    private static Task WithValidationAsync(Func<Task> body)
    {
        return WithModeAsync(decodeWithoutValidation: false, body);
    }

    private static async Task WithModeAsync(bool decodeWithoutValidation, Func<Task> body)
    {
        var previous = BearerValidationMode.DecodeWithoutValidation;
        BearerValidationMode.DecodeWithoutValidation = decodeWithoutValidation;

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
