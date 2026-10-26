using System.Net;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public class BearerAuthenticationMiddlewareTests
{
    private const string EntraTenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    private const string EntraObjectId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string EntraAccountId = $"{EntraTenantId}:{EntraObjectId}";

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
            context.Items.ContainsKey(FunctionContextItemKeys.MachineCredentialKind).ShouldBeFalse();
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
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.ClientCredentials);
            context.Items[FunctionContextItemKeys.CachedClaims].ShouldBeSameAs(claims);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineOrganization).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineProject).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Invoke_MachineResultNamingItsKindAndProject_StoresThem()
    {
        await WithValidationAsync(async () =>
        {
            var claims = new List<Claim> { new("azp", "machine-app"), new("sub", "machine-app") };
            var validator = new RecordingBearerTokenValidator
            {
                Result = new BearerValidationResult(claims, true, "machine-app", MachineCredentialKind.Federated, "org1", "proj1"),
            };
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, _) = FunctionTestDoubles.CreateRequest(services, Bearer("machine-token"));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.Federated);
            context.Items[FunctionContextItemKeys.MachineOrganization].ShouldBe("org1");
            context.Items[FunctionContextItemKeys.MachineProject].ShouldBe("proj1");
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
            var (context, request) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt($$"""{"sub":"user-1","azp":"api-client","tid":"{{EntraTenantId}}","oid":"{{EntraObjectId}}"}""")));

            await InvokeAsync(context);

            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            request.GetIdentityUniqueId().ShouldBe(EntraAccountId);
        });
    }

    // Decoded Entra claims are keyed as validated ones: sub becomes {tid}:{oid} in lower case, and the pairwise sub
    // stays as entra_sub.
    [Fact]
    public async Task Invoke_DebugDecode_EntraUser_IsKeyedByTenantAndObjectId()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, request) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt($$"""{"sub":"user-1","tid":"{{EntraTenantId.ToUpperInvariant()}}","oid":"{{EntraObjectId.ToUpperInvariant()}}","name":"Ada"}""")));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            validator.Calls.ShouldBe(0);
            request.GetIdentityUniqueId().ShouldBe(EntraAccountId);
            request.GetClaim(UserIdentity.EntraSubjectClaimType).ShouldBe("user-1");
            request.GetAuditActor().ShouldBe(AuditActor.Account(EntraAccountId, "Ada"));
        });
    }

    // Without tid and oid there is no account: the claims keep no sub, and every handler that needs the account is 401.
    [Theory]
    [InlineData("""{"sub":"user-1","name":"Ada"}""")]
    [InlineData("""{"sub":"user-1","tid":"8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d"}""")]
    [InlineData("""{"sub":"user-1","oid":"3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d"}""")]
    public async Task Invoke_DebugDecode_EntraUserWithoutTenantOrObjectId_HasNoAccountId(string payload)
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator);
            var (context, request) = FunctionTestDoubles.CreateRequest(services, Bearer(UnsignedJwt(payload)));

            await InvokeAsync(context);

            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            Should.Throw<SecurityTokenException>(() => request.GetIdentityUniqueId());
        });
    }

    [Fact]
    public async Task Invoke_DebugDecode_DelegatedTokenOfListedClient_IsNotAMachine()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator, new UserAuthenticationOptions
            {
                ClientId = "api-client",
                DelegatedClients = [new DelegatedClientOptions { ClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11", TenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d" }],
            });
            var (context, _) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt("""{"sub":"user-1","azp":"6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11","tid":"8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d","oid":"3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d","scp":"User.Impersonation"}""")));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeTrue();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Invoke_DebugDecode_AppOnlyTokenOfListedClient_IsAMachine()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var validator = new RecordingBearerTokenValidator();
            using var services = FunctionTestDoubles.CreateServices(validator, new UserAuthenticationOptions
            {
                ClientId = "api-client",
                DelegatedClients = [new DelegatedClientOptions { ClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11", TenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d" }],
            });
            var (context, _) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt("""{"sub":"6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11","azp":"6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11","tid":"8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d","roles":["Configuration.Read"]}""")));

            await InvokeAsync(context);

            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11");
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

    // The role-driven mode: no default user scopes, and a WorkOS permission that maps to User.Impersonation. Without the
    // permission the token does not act for a user.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoke_DebugDecode_AuthKitUserPermission_GivesUserImpersonation(bool hasPermission)
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
                    PermissionMap = new Dictionary<string, string>
                    {
                        ["storyteller:user"] = Scopes.User.Impersonation,
                        ["storyteller:annotation-read"] = Scopes.Annotation.Read,
                    },
                },
            });
            var permissions = hasPermission ? """["storyteller:user","storyteller:annotation-read"]""" : """["storyteller:annotation-read"]""";
            var (context, request) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt($$"""{"sub":"user_01","email":"ada@example.com","permissions":{{permissions}}}""")));

            await InvokeAsync(context);

            request.GetScopes().Contains(Scopes.User.Impersonation).ShouldBe(hasPermission);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
        });
    }

    // A WorkOS M2M token carries permission slugs in scope; the map turns each into its scope string, the carried half
    // of the scope checks. Unmapped slugs give nothing.
    [Fact]
    public async Task Invoke_DebugDecode_AuthKitMachineToken_CarriesTheMappedScopeStrings()
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
                    PermissionMap = new Dictionary<string, string>
                    {
                        ["storyteller:annotation-read"] = Scopes.Annotation.Read,
                        ["storyteller:configuration-write"] = Scopes.Configuration.Write,
                    },
                },
            });
            var (context, request) = FunctionTestDoubles.CreateRequest(
                services,
                Bearer(UnsignedJwt("""{"sub":"client_01M2M","client_id":"client_01M2M","scope":"storyteller:annotation-read storyteller:configuration-write storyteller:unknown"}""")));

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("client_01M2M");
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.ClientCredentials);
            request.GetScopes().ShouldBe([Scopes.Annotation.Read, Scopes.Configuration.Write], ignoreOrder: true);
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
