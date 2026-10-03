using System.Net;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class BearerAuthenticationMiddlewareTests
{
    private readonly RecordingMiddleware _middleware = new();

    [Fact]
    public async Task Invoke_NonHttpInvocation_CallsNextWithoutValidating()
    {
        var validator = new RecordingBearerTokenValidator();
        using var services = FunctionTestDoubles.CreateServices(validator);
        var context = FunctionTestDoubles.CreateNonHttp(services);

        var called = await InvokeAsync(context);

        called.ShouldBeTrue();
        validator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Invoke_CachedClaims_PassesThrough()
    {
        await WithValidationAsync(async () =>
        {
            var validator = new RecordingBearerTokenValidator { Error = new InvalidOperationException("should not be called") };
            var cached = new List<Claim> { new("sub", "cached-user") };
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("ignored"));
            context.Items[FunctionContextItemKeys.CachedClaims] = cached;
            context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            validator.Calls.ShouldBe(0);
            context.Items[FunctionContextItemKeys.CachedClaims].ShouldBeSameAs(cached);
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("machine-1");
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ApiKey 2s.abc.def")]
    public async Task Invoke_WithoutBearerScheme_PassesThrough(string? authorization)
    {
        await WithValidationAsync(async () =>
        {
            var validator = new RecordingBearerTokenValidator { Error = new InvalidOperationException("should not be called") };
            using var services = FunctionTestDoubles.CreateServices(validator);
            IDictionary<string, string>? headers = authorization is null
                ? null
                : new Dictionary<string, string> { ["Authorization"] = authorization };
            var (context, _) = FunctionTestDoubles.CreateRequest(services, headers);

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            validator.Calls.ShouldBe(0);
            _middleware.Response.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Invoke_InvalidToken_ReturnsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("not-a-token"));

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            validator.Calls.ShouldBe(1);
            validator.LastToken.ShouldBe("not-a-token");
            var response = _middleware.Response.ShouldBeOfType<TestHttpResponseData>();
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Invoke_KeyRetrievalFailure_ReturnsServiceUnavailable()
    {
        await WithValidationAsync(async () =>
        {
            var validator = new RecordingBearerTokenValidator
            {
                Error = new BearerKeyRetrievalException("keys unavailable", new HttpRequestException("down")),
            };
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("token"));

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            var response = _middleware.Response.ShouldBeOfType<TestHttpResponseData>();
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            response.Headers.TryGetValues("Retry-After", out var retryAfter).ShouldBeTrue();
            retryAfter.ShouldNotBeNull();
            retryAfter.ShouldContain("5");
        });
    }

    [Fact]
    public async Task Invoke_ValidUserToken_StoresClaimsAndCallsNext()
    {
        await WithValidationAsync(async () =>
        {
            var claims = new List<Claim> { new("sub", "user-1"), new("name", "Ada") };
            var validator = new RecordingBearerTokenValidator
            {
                Result = new BearerValidationResult(claims, false, null),
            };
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("user-token"));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            context.Items[FunctionContextItemKeys.CachedClaims].ShouldBeSameAs(claims);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            _middleware.Response.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Invoke_ValidMachineToken_StoresMachineIdentity()
    {
        await WithValidationAsync(async () =>
        {
            var claims = new List<Claim> { new("azp", "machine-app"), new("sub", "machine-app") };
            var validator = new RecordingBearerTokenValidator
            {
                Result = new BearerValidationResult(claims, true, "machine-app"),
            };
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("machine-token"));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("machine-app");
            context.Items[FunctionContextItemKeys.CachedClaims].ShouldBeSameAs(claims);
        });
    }

    [Fact]
    public async Task Invoke_DebugDecode_ReadsClaimsWithoutCallingTheValidator()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator { Error = new InvalidOperationException("should not be called") };
            using var services = FunctionTestDoubles.CreateServices(validator, new UserAuthenticationOptions
            {
                ClientId = "api-client",
            });
            var (context, _) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt("""{"sub":"user-1","azp":"machine-9","name":"Ada","preferred_username":"ada@example.com"}""")));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            validator.Calls.ShouldBe(0);
            var claims = context.Items[FunctionContextItemKeys.CachedClaims].ShouldBeAssignableTo<IReadOnlyList<Claim>>();
            claims.ShouldNotBeNull();
            claims.ShouldContain(claim => claim.Type == "sub" && claim.Value == "user-1");
            claims.ShouldContain(claim => claim.Type == "preferred_username" && claim.Value == "ada@example.com");
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("machine-9");
        });
    }

    [Fact]
    public async Task Invoke_DebugDecode_AzpMatchingClientId_IsNotAMachine()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator, new UserAuthenticationOptions
            {
                ClientId = "API-CLIENT",
            });
            var (context, _) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt("""{"sub":"user-1","azp":"api-client"}""")));

            await InvokeAsync(context);

            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Invoke_DebugDecode_UnreadableBearer_PassesThrough()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("not-a-jwt"));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            validator.Calls.ShouldBe(0);
            _middleware.Response.ShouldBeNull();
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Invoke_DebugDecode_AuthKitToken_RunsTheAuthKitNormalizer()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator, new UserAuthenticationOptions
            {
                Provider = IdentityProviderKind.AuthKit,
                AuthKit = new AuthKitOptions
                {
                    ClientId = "client_123",
                    DefaultUserScopes = ["User.Impersonation"],
                    PermissionMap = new Dictionary<string, string> { ["storyteller:annotation-read"] = "Annotation.Read" },
                },
            });
            var (context, _) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt("""{"sub":"user_01","email":"ada@example.com","first_name":"Ada","last_name":"Lovelace","azp":"someone","permissions":["storyteller:annotation-read"]}""")));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            validator.Calls.ShouldBe(0);
            var claims = context.Items[FunctionContextItemKeys.CachedClaims].ShouldBeAssignableTo<IReadOnlyList<Claim>>();
            claims.ShouldNotBeNull();
            claims.ShouldContain(claim => claim.Type == "sub" && claim.Value == "user_01");
            claims.ShouldContain(claim => claim.Type == "name" && claim.Value == "Ada Lovelace");
            claims.ShouldContain(claim => claim.Type == "preferred_username" && claim.Value == "ada@example.com");
            claims.ShouldContain(claim => claim.Type == "scp" && claim.Value == "User.Impersonation Annotation.Read");
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Invoke_CancellationDuringValidation_Propagates()
    {
        await WithValidationAsync(async () =>
        {
            var validator = new RecordingBearerTokenValidator { Error = new OperationCanceledException() };
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("token"));

            await Should.ThrowAsync<OperationCanceledException>(() => InvokeAsync(context));
        });
    }

    private Task<bool> InvokeAsync(TestFunctionContext context)
    {
        var called = false;
        return InvokeCoreAsync();

        async Task<bool> InvokeCoreAsync()
        {
            await _middleware.Invoke(context, _ =>
            {
                called = true;
                return Task.CompletedTask;
            });
            return called;
        }
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

    private static Dictionary<string, string> Bearer(string token)
    {
        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}",
        };
    }

    private static string UnsignedJwt(string payloadJson)
    {
        return string.Join('.', Base64Url("""{"alg":"none","typ":"JWT"}"""), Base64Url(payloadJson), "x");
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
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
