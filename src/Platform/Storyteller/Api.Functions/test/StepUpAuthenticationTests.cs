using System.Globalization;
using System.Security.Claims;
using System.Text;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The bearer middleware judges the sign-in of every user it stores against Auth:StepUp (424-264): from the auth_time of a
// provider token, or the one a Storyteller user token copied from its subject token; machines get no judgement.
// BearerValidationMode.DecodeWithoutValidation is static: the classes that switch it never run in parallel.
[Collection(nameof(BearerValidationMode))]
public class StepUpAuthenticationTests
{
    private const string StorytellerIssuer = "https://storyteller.example";
    private const string StorytellerAudience = "https://storyteller.example/api";
    private const string ProviderIssuer = "https://api.workos.com/user_management/client_01";
    private const string AccountId = "user_01";
    private const string ActorId = "mcp";

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private static bool DevAuth => Security.HttpRequestDataExtensions.IsDevelopmentAuthentication;

    private readonly RecordingMiddleware _middleware = new();
    private readonly ManualTimeProvider _time = new();
    private readonly FederationAccessService _access = new();
    private readonly TokenExchangeOptions _exchangeOptions = new()
    {
        Enabled = true,
        Actors = [new TokenExchangeActorOptions { Id = ActorId, SubjectAudiences = ["https://mcp.example/mcp"] }],
    };

    public StepUpAuthenticationTests()
    {
        _access.Accounts[AccountId] = new Account
        {
            Id = AccountId,
            UserName = "ada@example.com",
            Name = "Ada Lovelace",
            AccessMap = new Dictionary<string, AccountRole>(),
        };
    }

    public static TheoryData<int?, bool> SignInAges => new()
    {
        { 60, true },
        { 600, true },
        { 601, false },
        { 3600, false },
        { -60, true },
        { -300, true },
        { -301, false },
        { null, false },
    };

    [Theory]
    [MemberData(nameof(SignInAges))]
    public async Task ProviderUser_GetsTheJudgementOfItsAuthTime(int? ageSeconds, bool recent)
    {
        await WithValidationAsync(async () =>
        {
            var claims = UserClaims(ageSeconds is { } age ? AuthTime(_time.Now.AddSeconds(-age)) : null);
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult(claims, false, null) };
            var (context, _) = CreateRequest(user, ProviderToken(), new StepUpOptions { Enabled = true });

            (await InvokeAsync(context)).ShouldBeTrue();

            var recency = context.Items[FunctionContextItemKeys.SignInRecency].ShouldBeOfType<SignInRecency>();
            recency.IsRecent.ShouldBe(recent);
            recency.MaxAge.ShouldBe(MaxAge);
        });
    }

    // A provider token that names an actor (act: a WorkOS impersonation) carries the impersonator's sign-in, so it never
    // counts as the person's recent sign-in, however fresh its auth_time.
    [Fact]
    public async Task ProviderUser_WithAnActor_IsNeverRecent()
    {
        await WithValidationAsync(async () =>
        {
            var claims = UserClaims(AuthTime(_time.Now.AddSeconds(-30)));
            claims.Add(new Claim("act", """{"sub":"admin@example.com"}"""));
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult(claims, false, null) };
            var (context, request) = CreateRequest(user, ProviderToken(), new StepUpOptions { Enabled = true });

            (await InvokeAsync(context)).ShouldBeTrue();

            request.HasRecentSignIn().ShouldBeFalse();
        });
    }

    // The MCP server's Storyteller user token carries the person's sign-in from the subject token: an old one is stale,
    // a fresh one recent, a missing one stale.
    [Theory]
    [InlineData(120, true)]
    [InlineData(1200, false)]
    [InlineData(null, false)]
    public async Task DelegatedUser_GetsTheJudgementOfTheCopiedAuthTime(int? ageSeconds, bool recent)
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Error = new InvalidOperationException("should not be called") };
            var storyteller = new RecordingUserTokenIssuerValidator(StorytellerIssuer)
            {
                Result = new BearerValidationResult(
                    [new Claim("sub", AccountId), new Claim("scp", Scopes.User.Impersonation), .. ageSeconds is { } age ? new[] { AuthTime(_time.Now.AddSeconds(-age)) } : []],
                    false,
                    null,
                    Delegation: new BearerDelegation(ActorId, [ActorRestrictions.NoSecretReveal])),
            };
            var (context, request) = CreateRequest(user, StorytellerToken(), new StepUpOptions { Enabled = true }, storyteller);

            (await InvokeAsync(context)).ShouldBeTrue();

            request.TryGetDelegatedActor(out var actorId).ShouldBeTrue();
            actorId.ShouldBe(ActorId);
            request.GetSignInRecency().IsRecent.ShouldBe(recent);
        });
    }

    // Machines never need a step-up: the middleware stores no judgement, and the request reads as recent.
    [Fact]
    public async Task Machine_GetsNoJudgement()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator
            {
                Result = new BearerValidationResult([new Claim("sub", "machine-1"), new Claim("roles", Scopes.Configuration.Read)], true, "machine-1"),
            };
            var (context, request) = CreateRequest(user, ProviderToken(), new StepUpOptions { Enabled = true, MaxAge = TimeSpan.FromMinutes(1) });

            (await InvokeAsync(context)).ShouldBeTrue();

            context.Items.ContainsKey(FunctionContextItemKeys.SignInRecency).ShouldBeFalse();
            request.HasRecentSignIn().ShouldBeTrue();
            Should.NotThrow(() => request.EnsureRecentSignIn());
        });
    }

    // Turned off, and under Entra ID by default, every user reads as recent, also without auth_time.
    [Theory]
    [InlineData(IdentityProviderKind.AuthKit, false)]
    [InlineData(IdentityProviderKind.EntraId, null)]
    public async Task StepUpOff_EveryUserIsRecent(IdentityProviderKind provider, bool? enabled)
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult(UserClaims(null), false, null) };
            var (context, request) = CreateRequest(user, ProviderToken(), new StepUpOptions { Enabled = enabled }, provider: provider);

            (await InvokeAsync(context)).ShouldBeTrue();

            request.GetSignInRecency().ShouldBe(SignInRecency.NotRequired(MaxAge));
        });
    }

    // Under AuthKit and Keycloak the default is on in Release builds; Debug builds (DEV_AUTH) skip it unless it is set.
    [Fact]
    public async Task AuthKitByDefault_JudgesInReleaseBuildsOnly()
    {
        await WithValidationAsync(async () =>
        {
            var user = new RecordingBearerTokenValidator { Result = new BearerValidationResult(UserClaims(null), false, null) };
            var (context, request) = CreateRequest(user, ProviderToken(), new StepUpOptions(), provider: IdentityProviderKind.AuthKit);

            (await InvokeAsync(context)).ShouldBeTrue();

            request.HasRecentSignIn().ShouldBe(DevAuth);
        });
    }

    // Debug builds decode tokens they do not validate (DEV_AUTH); when the step-up is set on, the decoded auth_time is
    // judged as a validated one.
    [Fact]
    public async Task DecodedUser_WithTheStepUpSetOn_GetsTheJudgementToo()
    {
        await WithModeAsync(decodeWithoutValidation: true, async () =>
        {
            var token = UnsignedJwt($$"""{"iss":"{{ProviderIssuer}}","sub":"{{AccountId}}","scp":"User.Impersonation","auth_time":{{_time.Now.AddHours(-1).ToUnixTimeSeconds()}}}""");
            var (context, request) = CreateRequest(new RecordingBearerTokenValidator(), token, new StepUpOptions { Enabled = true });

            (await InvokeAsync(context)).ShouldBeTrue();

            request.GetSignInRecency().IsRecent.ShouldBeFalse();
        });
    }

    // On by default only under AuthKit, and only in Release builds; Keycloak needs it set.
    [Theory]
    [InlineData(IdentityProviderKind.AuthKit, false, null, true)]
    [InlineData(IdentityProviderKind.AuthKit, true, null, false)]
    [InlineData(IdentityProviderKind.Keycloak, false, null, false)]
    [InlineData(IdentityProviderKind.Keycloak, false, true, true)]
    [InlineData(IdentityProviderKind.EntraId, false, true, true)]
    public void Evaluate_WithoutAnAuthTime_FollowsTheOptions(IdentityProviderKind provider, bool developmentAuthentication, bool? enabled, bool judged)
    {
        var claims = new List<Claim> { new("sub", AccountId) };

        var recency = SignInRecency.Evaluate(claims, new StepUpOptions { Enabled = enabled }, provider, _time.Now, developmentAuthentication);

        recency.IsRecent.ShouldBe(!judged);
    }

    // A request that never passed the bearer middleware: Release builds find no recent sign-in, Debug builds need none.
    [Fact]
    public void UserWithoutAJudgement_IsStaleInReleaseBuildsOnly()
    {
        var request = TestRequests.User().WithSignIn(null);

        request.HasRecentSignIn().ShouldBe(DevAuth);
        request.GetSignInRecency().MaxAge.ShouldBe(StepUpOptions.DefaultMaxAge);
    }

    [Fact]
    public void EnsureRecentSignIn_Stale_ThrowsTheStepUpWithTheWindow()
    {
        var request = TestRequests.User().WithSignIn(SignInRecency.Stale(TimeSpan.FromMinutes(5)));

        var exception = Should.Throw<StepUpRequiredException>(() => request.EnsureRecentSignIn());

        exception.ErrorCode.ShouldBe(ErrorCodes.StepUpRequired);
        exception.MaxAge.ShouldBe(TimeSpan.FromMinutes(5));
        Should.NotThrow(() => TestRequests.User().EnsureRecentSignIn());
    }

    private static Claim AuthTime(DateTimeOffset time)
    {
        return new Claim("auth_time", time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);
    }

    private static List<Claim> UserClaims(Claim? authTime)
    {
        var claims = new List<Claim> { new("sub", AccountId), new("scp", Scopes.User.Impersonation) };

        if (authTime is not null)
        {
            claims.Add(authTime);
        }

        return claims;
    }

    private static string StorytellerToken()
    {
        return UnsignedJwt($$"""{"iss":"{{StorytellerIssuer}}","aud":"{{StorytellerAudience}}","sub":"{{AccountId}}","act":{"sub":"mcp"},"token_use":"user"}""");
    }

    private static string ProviderToken()
    {
        return UnsignedJwt($$"""{"iss":"{{ProviderIssuer}}","aud":"https://2s.example","sub":"{{AccountId}}"}""");
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
        StepUpOptions stepUp,
        IMachineTokenValidator? machine = null,
        IdentityProviderKind provider = IdentityProviderKind.AuthKit)
    {
        var options = new UserAuthenticationOptions { Provider = provider };
        options.AuthKit.ClientId = "client_01";
        var services = machine is null
            ? FunctionTestDoubles.CreateServiceCollection(user, options)
            : FunctionTestDoubles.CreateServiceCollection(user, options, machine);
        services.AddSingleton<IAccessService>(_access);
        services.AddSingleton(Options.Create(_exchangeOptions));
        services.AddSingleton(Options.Create(stepUp));
        services.AddSingleton<TimeProvider>(_time);
        return FunctionTestDoubles.CreateRequest(services.BuildServiceProvider(), new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" });
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
