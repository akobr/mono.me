using System.Net;
using System.Security.Claims;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Storyteller user tokens on API routes: only the Storyteller token validator (IUserTokenIssuerValidator) may return a
// user, with the actor that acts for it. The middleware stores the user as a user (no machine identity) with the actor
// and its restrictions as items, and names it from its account; a token whose account is gone is 401. Tokens a provider
// issued to an actor (its SubjectAudiences) are refused on API routes, validated or decoded.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public class StorytellerUserTokenAuthenticationTests
{
    private const string StorytellerIssuer = "https://storyteller.example";
    private const string StorytellerAudience = "https://storyteller.example/api";
    private const string ProviderIssuer = "https://login.microsoftonline.com/8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d/v2.0";
    private const string AccountId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string ActorId = "mcp";
    private const string McpResource = "https://mcp.example/mcp";

    private readonly RecordingMiddleware _middleware = new();
    private readonly FederationAccessService _access = new();
    private readonly TokenExchangeOptions _exchangeOptions = new()
    {
        Enabled = true,
        Actors = [new TokenExchangeActorOptions { Id = ActorId, SubjectAudiences = [McpResource, "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11"] }],
    };

    public StorytellerUserTokenAuthenticationTests()
    {
        _access.Accounts[AccountId] = new Account
        {
            Id = AccountId,
            UserName = "ada@example.com",
            Name = "Ada Lovelace",
            AccessMap = new Dictionary<string, AccountRole>(),
        };
    }

    public static TheoryData<string> AudiencesOfTheActor => new()
    {
        { $"\"{McpResource}\"" },
        { $"\"{McpResource}/\"" },
        { $"\"{McpResource.ToUpperInvariant()}\"" },
        { $"\" {McpResource} \"" },
        { "\"6C0F1A2E-1F43-4B0E-9A4D-2F5E8F0C9B11\"" },
        { $"[\"api://client-id\",\"{McpResource}\"]" },
    };

    [Fact]
    public async Task UserToken_IsStoredAsTheUser_WithTheActorItsRestrictionsAndTheNameOfTheAccount()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Error = new InvalidOperationException("should not be called") };
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer) { Result = UserResult() };
            var (context, request) = CreateRequest(user, StorytellerToken(), storyteller);

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            storyteller.Calls.ShouldBe(1);
            user.Calls.ShouldBe(0);
            context.Items.ContainsKey(FunctionContextItemKeys.MachineIdentity).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.MachineCredentialKind).ShouldBeFalse();
            context.Items[FunctionContextItemKeys.DelegatedActor].ShouldBe(ActorId);
            context.Items[FunctionContextItemKeys.DelegatedRestrictions].ShouldBeAssignableTo<IReadOnlyList<string>>()
                .ShouldBe([ActorRestrictions.NoSecretReveal]);
            request.IsApplicationIdentity().ShouldBeFalse();
            request.GetIdentityUniqueId().ShouldBe(AccountId);
            request.GetClaim("name").ShouldBe("Ada Lovelace");
            request.GetClaim("preferred_username").ShouldBe("ada@example.com");
            request.GetScopes().ShouldBe([Scopes.User.Impersonation]);
            request.TryGetDelegatedActor(out var actorId).ShouldBeTrue();
            actorId.ShouldBe(ActorId);
            request.HasActorRestriction(ActorRestrictions.NoSecretReveal).ShouldBeTrue();
            request.GetAuditActor().ShouldBe(AuditActor.Account(AccountId, "Ada Lovelace") with { Via = ActorId });
        });
    }

    // The account is the source of the names: whatever name claims the result carries are replaced.
    [Fact]
    public async Task UserToken_NamesOfTheResult_AreReplacedByTheAccounts()
    {
        await WithValidationAsync(async () =>
        {
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer)
            {
                Result = UserResult(new Claim("name", "Mallory"), new Claim("preferred_username", "mallory@example.com")),
            };
            var (context, request) = CreateRequest(new RecordingBearerTokenValidator(), StorytellerToken(), storyteller);

            (await InvokeAsync(context)).ShouldBeTrue();

            request.GetClaims().Where(claim => claim.Type == "name").ShouldHaveSingleItem().Value.ShouldBe("Ada Lovelace");
            request.GetClaims().Where(claim => claim.Type == "preferred_username").ShouldHaveSingleItem().Value.ShouldBe("ada@example.com");
        });
    }

    [Fact]
    public async Task UserToken_OfNoAccount_IsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            _access.Accounts.Clear();
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer) { Result = UserResult() };
            var (context, _) = CreateRequest(new RecordingBearerTokenValidator(), StorytellerToken(), storyteller);

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedActor).ShouldBeFalse();
        });
    }

    // A user of the Storyteller issuer always acts through an actor; without one it is refused.
    [Fact]
    public async Task UserResultWithoutADelegation_IsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer) { Result = UserResult() with { Delegation = null } };
            var (context, _) = CreateRequest(new RecordingBearerTokenValidator(), StorytellerToken(), storyteller);

            (await InvokeAsync(context)).ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    // Only an IUserTokenIssuerValidator may return users: any other owner's user result is 401, delegation or not.
    [Fact]
    public async Task UserResultOfAnotherOwner_IsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            var owner = new RecordingMachineTokenValidator(StorytellerIssuer) { Owns = true, Result = UserResult() };
            var (context, _) = CreateRequest(new RecordingBearerTokenValidator(), StorytellerToken(), owner);

            (await InvokeAsync(context)).ShouldBeFalse();
            owner.Calls.ShouldBe(1);
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedActor).ShouldBeFalse();
        });
    }

    // The machines of the Storyteller validator stay machines, and a delegation never comes with them.
    [Fact]
    public async Task MachineResultOfTheStorytellerValidator_IsAMachineWithoutADelegation()
    {
        await WithValidationAsync(async () =>
        {
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer)
            {
                Result = new BearerValidationResult(
                    [new Claim("azp", "machine-1"), new Claim("sub", "machine-1")],
                    true,
                    "machine-1",
                    MachineCredentialKind.Federated,
                    "org1",
                    "proj1",
                    new BearerDelegation(ActorId, [])),
            };
            var (context, _) = CreateRequest(new RecordingBearerTokenValidator(), StorytellerToken(), storyteller);

            (await InvokeAsync(context)).ShouldBeTrue();

            context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe("machine-1");
            context.Items[FunctionContextItemKeys.MachineCredentialKind].ShouldBe(MachineCredentialKind.Federated);
            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedActor).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedRestrictions).ShouldBeFalse();
        });
    }

    // A delegation is taken from the Storyteller validator only; a provider's result is a plain user.
    [Fact]
    public async Task DelegationOfAProviderResult_IsIgnored()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = UserResult() };
            var (context, request) = CreateRequest(user, ProviderToken("\"api://client-id\""));

            (await InvokeAsync(context)).ShouldBeTrue();

            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedActor).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedRestrictions).ShouldBeFalse();
            request.GetAuditActor().Via.ShouldBeNull();
        });
    }

    // DEV_AUTH decodes the tokens of other issuers, but a Storyteller token is validated in every build.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserToken_UnderDebugDecoding_IsStillValidated(bool accepted)
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer) { Result = accepted ? UserResult() : null };
            var (context, _) = CreateRequest(new RecordingBearerTokenValidator(), StorytellerToken(), storyteller);

            var called = await InvokeAsync(context);

            storyteller.Calls.ShouldBe(1);
            called.ShouldBe(accepted);

            if (accepted)
            {
                context.Items[FunctionContextItemKeys.DelegatedActor].ShouldBe(ActorId);
            }
            else
            {
                _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }
        });
    }

    // Decoded claims of another issuer never give a delegation, whatever act or restrictions the token claims.
    [Fact]
    public async Task DecodedTokenThatClaimsAnActor_GivesNoDelegation()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var token = UnsignedJwt($$"""{"iss":"{{ProviderIssuer}}","aud":"api://client-id","sub":"user-1","tid":"8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d","oid":"3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d","act":{"sub":"mcp"},"restrictions":[]}""");
            var (context, request) = CreateRequest(new RecordingBearerTokenValidator(), token);

            (await InvokeAsync(context)).ShouldBeTrue();

            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedActor).ShouldBeFalse();
            context.Items.ContainsKey(FunctionContextItemKeys.DelegatedRestrictions).ShouldBeFalse();
            request.TryGetDelegatedActor(out _).ShouldBeFalse();
            request.GetAuditActor().Via.ShouldBeNull();
        });
    }

    // A provider token issued to an actor (one of its SubjectAudiences) is for the exchange, even when valid: passing it on
    // is token passthrough. Compared without a trailing '/' and ignoring case, against any of its audiences.
    [Theory]
    [MemberData(nameof(AudiencesOfTheActor))]
    public async Task ProviderTokenIssuedToAnActor_IsUnauthorized(string audience)
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult([new Claim("sub", AccountId)], false, null) };
            var (context, _) = CreateRequest(user, ProviderToken(audience));

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            user.Calls.ShouldBe(1);
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
        });
    }

    [Theory]
    [MemberData(nameof(AudiencesOfTheActor))]
    public async Task DecodedTokenIssuedToAnActor_IsUnauthorized(string audience)
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var user = new RecordingBearerTokenValidator();
            var (context, _) = CreateRequest(user, ProviderToken(audience));

            var called = await InvokeAsync(context);

            called.ShouldBeFalse();
            user.Calls.ShouldBe(0);
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
        });
    }

    // The actors are trusted only while the exchange is enabled, but their tokens are never bearer tokens of the API.
    [Fact]
    public async Task ProviderTokenIssuedToAnActor_IsUnauthorized_AlsoWhileTheExchangeIsDisabled()
    {
        await WithValidationAsync(async () =>
        {
            _exchangeOptions.Enabled = false;
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult([new Claim("sub", AccountId)], false, null) };
            var (context, _) = CreateRequest(user, ProviderToken($"\"{McpResource}\""));

            (await InvokeAsync(context)).ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    // A machine token of the provider fallback that names an actor's audience is refused too.
    [Fact]
    public async Task MachineTokenIssuedToAnActor_IsUnauthorized()
    {
        await WithValidationAsync(async () =>
        {
            var machine = new RecordingMachineTokenValidator(ProviderIssuer)
            {
                Result = new BearerValidationResult([new Claim("azp", "machine-client")], true, "machine-client"),
            };
            var (context, _) = CreateRequest(new RecordingBearerTokenValidator(), ProviderToken($"[\"api://client-id\",\"{McpResource}\"]"), machine);

            (await InvokeAsync(context)).ShouldBeFalse();
            machine.Calls.ShouldBe(1);
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderTokenOfAnotherAudience_Passes(bool decodeWithoutValidation)
    {
        await WithModeAsync(decodeWithoutValidation, async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult([new Claim("sub", AccountId)], false, null) };
            var (context, _) = CreateRequest(user, ProviderToken($"[\"api://client-id\",\"{McpResource}/other\"]"));

            (await InvokeAsync(context)).ShouldBeTrue();
            _middleware.Response.ShouldBeNull();
        });
    }

    // The 503 of unavailable keys stays: the audience only counts for a token a validator accepted.
    [Fact]
    public async Task ProviderTokenIssuedToAnActor_WhenTheKeysAreUnavailable_IsServiceUnavailable()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Error = new BearerKeyRetrievalException("down", new HttpRequestException("down")) };
            var (context, _) = CreateRequest(user, ProviderToken($"\"{McpResource}\""));

            (await InvokeAsync(context)).ShouldBeFalse();
            _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        });
    }

    // End to end: a token of the issuer passes the validator and the middleware as the user, acting through the actor,
    // the user checks (User.Impersonation in scp, no machine identity) hold, and its NoSecretReveal withholds secret reads.
    [Fact]
    public async Task IssuedUserToken_PassesTheStorytellerValidatorAndTheMiddleware()
    {
        await WithValidationAsync(async () =>
        {
            using var services = BuildFederationServices();
            var issued = await services.GetRequiredService<StorytellerTokenIssuer>().IssueUserAsync(new StorytellerUserTokenRequest
            {
                AccountId = AccountId,
                ActorId = ActorId,
                Scopes = [UserScopes.Impersonation],
                Restrictions = [ActorRestrictions.NoSecretReveal],
                MaxLifetime = TimeSpan.FromMinutes(10),
                NotAfter = DateTimeOffset.UtcNow.AddHours(1),
            });
            var (context, request) = FunctionTestDoubles.CreateRequest(services, new Dictionary<string, string> { ["Authorization"] = $"Bearer {issued.AccessToken}" });

            var called = await InvokeAsync(context);

            called.ShouldBeTrue();
            context.Items[FunctionContextItemKeys.DelegatedActor].ShouldBe(ActorId);
            request.HasActorRestriction(ActorRestrictions.NoSecretReveal).ShouldBeTrue();
            request.GetIdentityUniqueId().ShouldBe(AccountId);
            request.GetAuditActor().ShouldBe(AuditActor.Account(AccountId, "Ada Lovelace") with { Via = ActorId });
            Should.NotThrow(() => request.EnsureUser());

            // the restriction of the token reaches the secret rights: the role reads secrets, the actor withholds them
            var rights = await request.GetSecretRightsAsync(new RoleAccess(AccountRole.ContributorWithSecrets), new FixedKeyValuePolicies(), "acme", "web");
            rights.CanReadSecrets.ShouldBeFalse();
            rights.CanWriteSecrets.ShouldBeTrue();
            rights.IsReadWithheldByActor.ShouldBeTrue();
        });
    }

    private static BearerValidationResult UserResult(params Claim[] claims)
    {
        return new BearerValidationResult(
            [new Claim("sub", AccountId), new Claim("scp", Scopes.User.Impersonation), .. claims],
            false,
            null,
            Delegation: new BearerDelegation(ActorId, [ActorRestrictions.NoSecretReveal]));
    }

    private static string StorytellerToken()
    {
        return UnsignedJwt($$"""{"iss":"{{StorytellerIssuer}}","aud":"{{StorytellerAudience}}","sub":"{{AccountId}}","act":{"sub":"mcp"},"token_use":"user"}""");
    }

    // A token of the user provider with the given aud (a JSON string or array); unsigned, as a validator double accepts it.
    private static string ProviderToken(string audience)
    {
        return UnsignedJwt($$"""{"iss":"{{ProviderIssuer}}","aud":{{audience}},"sub":"user-1","tid":"8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d","oid":"3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d","scp":"User.Impersonation"}""");
    }

    private static string UnsignedJwt(string payloadJson)
    {
        return string.Join('.', Base64Url("""{"alg":"none","typ":"JWT"}"""), Base64Url(payloadJson), "x");
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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

    private (TestFunctionContext Context, TestHttpRequestData Request) CreateRequest(
        RecordingBearerTokenValidator user,
        string token,
        params IMachineTokenValidator[] machines)
    {
        var services = FunctionTestDoubles.CreateServiceCollection(user, null, machines);
        services.AddSingleton<IAccessService>(_access);
        services.AddSingleton(Options.Create(_exchangeOptions));
        return FunctionTestDoubles.CreateRequest(services.BuildServiceProvider(), new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
    }

    // The registration of Program.cs for the Storyteller issuer, with the account and the actor of these tests.
    private ServiceProvider BuildFederationServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MachineAuth:Federation:Issuer"] = StorytellerIssuer,
                ["MachineAuth:Federation:Audience"] = StorytellerAudience,
            })
            .Build();
        var services = FunctionTestDoubles.CreateServiceCollection(new RecordingBearerTokenValidator());
        services.AddLogging();
        services.AddFederatedMachineAccess(configuration);
        services.AddSingleton<IAccessService>(_access);
        services.AddSingleton(Options.Create(_exchangeOptions));
        return services.BuildServiceProvider();
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
