using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Moq;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class FederatedTokenExchangeServiceTests : IDisposable
{
    private const string Organization = "org1";
    private const string Project = "proj1";
    private const string GitHub = "https://token.actions.githubusercontent.com";
    private const string Audience = "urn:test:storyteller";
    private const string MachineId = "0d8f6c1e-2b7a-4f0e-9a43-6c2e1b5d7f90";

    private readonly ManualTimeProvider _time = new();
    private readonly StubHttpMessageHandler _http = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TestSigningKey _key = TestSigningKey.Rsa("gh-1");
    private readonly Mock<IAccessService> _access = new();
    private readonly Mock<IFederatedIssuerStore> _issuers = new();
    private readonly CountingSigningKeyStore _signingKeys = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:2s.test", Audience = "urn:storyteller:2s.test:api" };
    private readonly MachineAuthenticationOptions _machineOptions = new();
    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly FederatedTokenExchangeService _exchange;

    public FederatedTokenExchangeServiceTests()
    {
        var options = Options.Create(_options);
        var resolver = new FederatedIssuerKeyResolver(new StubHttpClientFactory(_http), _cache, options, _time, NullLogger<FederatedIssuerKeyResolver>.Instance);
        var verifier = new SubjectTokenVerifier(resolver, options, _time, NullLogger<SubjectTokenVerifier>.Instance);
        _keyProvider = new TokenSigningKeyProvider(_signingKeys, options, _time, NullLogger<TokenSigningKeyProvider>.Instance);
        _exchange = new FederatedTokenExchangeService(
            _access.Object,
            _issuers.Object,
            verifier,
            new StorytellerTokenIssuer(_keyProvider, options, _time),
            options,
            Options.Create(_machineOptions),
            NullLogger<FederatedTokenExchangeService>.Instance);

        SetPolicy(MachineCredentialKind.ApiKey, MachineCredentialKind.Federated);
        SetIssuers(GitHubIssuer());
        _access.Setup(service => service.GetMachineAccessAsync(Organization, Project, MachineId)).ReturnsAsync(Machine());
    }

    public void Dispose()
    {
        _key.Dispose();
        _http.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task MatchingToken_IsExchangedForAStorytellerTokenOfTheMachine()
    {
        var result = await _exchange.ExchangeAsync(Request());

        result.Succeeded.ShouldBeTrue();
        result.IssuedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
        result.ExpiresIn.ShouldBe(900);
        result.Scope.ShouldBe("Default.Read Annotation.Read Configuration.Read");

        var token = new JsonWebToken(result.AccessToken);
        token.Subject.ShouldBe(MachineId);
        token.GetPayloadValue<string>("organization").ShouldBe(Organization);
        token.GetPayloadValue<string>("federation_issuer").ShouldBe("github");
        token.GetPayloadValue<string>("federation_subject").ShouldBe("repo:acme/app:ref:refs/heads/main");
        token.GetPayloadValue<string>("annotation_key").ShouldBe("unit.service");

        var validated = await new StorytellerTokenValidator(_keyProvider, Options.Create(_options), _time, NullLogger<StorytellerTokenValidator>.Instance)
            .ValidateAsync(result.AccessToken!);
        validated.ShouldNotBeNull();
        validated.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        validated.Project.ShouldBe(Project);
    }

    [Fact]
    public async Task RequestedTokenType_IsEchoed()
    {
        var result = await _exchange.ExchangeAsync(Request() with { RequestedTokenType = OAuthTokenExchange.TokenTypes.Jwt });

        result.IssuedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.Jwt);
    }

    [Fact]
    public async Task Scope_IsNarrowedToTheRequestedSubset()
    {
        var result = await _exchange.ExchangeAsync(Request() with { Scopes = ["Annotation.Read", "Annotation.Read"] });

        result.Succeeded.ShouldBeTrue();
        result.Scope.ShouldBe("Annotation.Read");
        new JsonWebToken(result.AccessToken).GetPayloadValue<string>("scope").ShouldBe("Annotation.Read");
    }

    [Fact]
    public async Task ScopeBeyondTheMachine_IsInvalidScope_AfterVerification()
    {
        var result = await _exchange.ExchangeAsync(Request() with { Scopes = ["Default.Read", "Configuration.Secrets"] });

        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidScope);
        result.ErrorDescription.ShouldBe(FederatedTokenExchangeService.ScopeExceedsMachineDescription);
        _signingKeys.Creates.ShouldBe(0);
    }

    // The management scopes of the machine are its scopes too, so a job may ask for one it needs.
    [Fact]
    public async Task ManagementScopeOfTheMachine_CanBeRequested()
    {
        WithManagementScopes(MachineManagementScope.ConfigurationTransfer, MachineManagementScope.SchemaWrite);

        var result = await _exchange.ExchangeAsync(Request() with { Scopes = ["Default.Read", "Configuration.Transfer"] });

        result.Succeeded.ShouldBeTrue();
        result.Scope.ShouldBe("Default.Read Configuration.Transfer");
        new JsonWebToken(result.AccessToken).GetPayloadValue<string>("scope").ShouldBe("Default.Read Configuration.Transfer");
    }

    [Fact]
    public async Task ManagementScopeTheMachineDoesNotHave_IsInvalidScope()
    {
        WithManagementScopes(MachineManagementScope.SchemaWrite);

        var result = await _exchange.ExchangeAsync(Request() with { Scopes = ["Default.Read", "Configuration.Transfer"] });

        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidScope);
        result.ErrorDescription.ShouldBe(FederatedTokenExchangeService.ScopeExceedsMachineDescription);
        _signingKeys.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task ManagementScope_OfAMachineWithoutAny_IsInvalidScope()
    {
        var result = await _exchange.ExchangeAsync(Request() with { Scopes = ["Configuration.Transfer"] });

        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidScope);
    }

    // Without scope, the token gets every scope of the machine: the preset's, then its management scopes in enum order.
    [Fact]
    public async Task WithoutScope_TheTokenCarriesTheManagementScopesToo()
    {
        WithManagementScopes(MachineManagementScope.KeyValueManage, MachineManagementScope.ConfigurationTransfer);

        var result = await _exchange.ExchangeAsync(Request());

        result.Succeeded.ShouldBeTrue();
        result.Scope.ShouldBe("Default.Read Annotation.Read Configuration.Read Configuration.Transfer KeyValue.Manage");
        new JsonWebToken(result.AccessToken).GetPayloadValue<string>("scope").ShouldBe(result.Scope);
    }

    [Fact]
    public async Task ScopeBeyondTheMachine_WithAMismatchedToken_IsNotAccepted()
    {
        // The scope is compared only after the token matched, so it reveals nothing about the machine.
        var result = await _exchange.ExchangeAsync(Request(Sign(owner: "666")) with { Scopes = ["Configuration.Secrets"] });

        ShouldBeNotAccepted(result);
    }

    [Fact]
    public async Task UnknownProject_IsNotAccepted_WithoutReadingIssuersOrTheMachine()
    {
        _access.Setup(service => service.GetAccessPointAsync(It.IsAny<string>())).ReturnsAsync((AccessPoint?)null);

        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request()));

        _issuers.Verify(store => store.ListAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoMachineRead();
    }

    [Fact]
    public async Task ProjectWithoutFederatedMachines_IsNotAccepted_WithoutReadingIssuersOrTheMachine()
    {
        SetPolicy(MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials);

        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request()));

        _issuers.Verify(store => store.ListAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoMachineRead();
    }

    [Fact]
    public async Task ProjectWithoutAStoredPolicy_FollowsTheDeploymentDefault()
    {
        _access.Setup(service => service.GetAccessPointAsync($"{Organization}.{Project}"))
            .ReturnsAsync(new AccessPoint { Key = $"{Organization}.{Project}", AccessMap = new Dictionary<string, AccountRole>() });

        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request()));

        _machineOptions.DefaultCredentialKind = MachineCredentialKind.Federated;
        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task UntrustedIssuer_IsNotAccepted_WithoutReadingTheMachine()
    {
        SetIssuers(GitHubIssuer() with { Issuer = "https://token.actions.githubusercontent.com/acme-enterprise" });

        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request()));

        VerifyNoMachineRead();
        _http.TotalRequests.ShouldBe(0);
    }

    [Fact]
    public async Task UnknownMachine_IsNotAccepted()
    {
        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request() with { MachineId = Guid.NewGuid().ToString("D") }));
    }

    [Fact]
    public async Task MachineOfAnotherKind_IsNotAccepted()
    {
        _access.Setup(service => service.GetMachineAccessAsync(Organization, Project, MachineId))
            .ReturnsAsync(Machine() with { CredentialKind = MachineCredentialKind.ApiKey });

        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request()));
    }

    [Fact]
    public async Task TokenMismatchingTheRules_IsNotAccepted()
    {
        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request(Sign(owner: "666"))));
        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request(Sign(subject: "repo:acme/other:ref:refs/heads/main"))));
        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request(Sign(audience: "https://github.com/acme"))));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c.d.e")]
    public async Task MalformedSubjectToken_IsNotAccepted_BeforeAnyRead(string token)
    {
        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request(token)));

        _access.Verify(service => service.GetAccessPointAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OversizedSubjectToken_IsNotAccepted_BeforeAnyRead()
    {
        _options.MaxSubjectTokenBytes = 1024;

        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request(Sign(padding: new string('x', 1024)))));

        _access.Verify(service => service.GetAccessPointAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("42.sform.org1.proj1.0d8f6c1e2b7a4f0e9a436c2e1b5d7f90")]
    [InlineData("client_01J")]
    public async Task ClientIdThatIsNotAFederatedMachineId_IsNotAccepted_BeforeAnyRead(string clientId)
    {
        ShouldBeNotAccepted(await _exchange.ExchangeAsync(Request() with { MachineId = clientId }));

        _access.Verify(service => service.GetAccessPointAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UnreachableIssuerKeys_AreTemporarilyUnavailable()
    {
        SetIssuers(new FederatedIssuer
        {
            Name = "github",
            Issuer = GitHub,
            KeySource = FederatedIssuerKeySource.Discovery,
            RequiredClaims = OwnerPin(),
        });
        _http.SetJson($"{GitHub}/.well-known/openid-configuration", "{}", System.Net.HttpStatusCode.BadGateway);

        var result = await _exchange.ExchangeAsync(Request());

        result.IsUnavailable.ShouldBeTrue();
        result.Error.ShouldBe(OAuthTokenExchange.Errors.TemporarilyUnavailable);
        result.ErrorDescription.ShouldBe(FederatedTokenExchangeResult.KeysUnavailableDescription);
    }

    [Fact]
    public async Task UnknownKidOfAFetchedKeySet_IsUnavailable_OnlyForAnExistingMachineWithThatRuleAudience()
    {
        // The documented exception to opaque refusals: this 503 comes after the project, issuer, machine and audience
        // checks, so it tells them apart. A token for an unknown machine or another audience gets the opaque 400.
        const string jwksUrl = "https://token.actions.githubusercontent.com/.well-known/jwks";
        SetIssuers(new FederatedIssuer { Name = "github", Issuer = GitHub, KeySource = FederatedIssuerKeySource.Discovery, RequiredClaims = OwnerPin() });
        _http.SetJson($"{GitHub}/.well-known/openid-configuration", System.Text.Json.JsonSerializer.Serialize(new { issuer = GitHub, jwks_uri = jwksUrl }));
        _http.SetJson(jwksUrl, TestSigningKey.Jwks(_key));
        var token = Sign(kid: "unpublished");

        var forTheMachine = await _exchange.ExchangeAsync(Request(token));
        var forAnUnknownMachine = await _exchange.ExchangeAsync(Request(token) with { MachineId = Guid.NewGuid().ToString("D") });
        var forAnotherAudience = await _exchange.ExchangeAsync(Request(Sign(audience: "urn:test:other", kid: "unpublished")));

        forTheMachine.IsUnavailable.ShouldBeTrue();
        forTheMachine.Error.ShouldBe(OAuthTokenExchange.Errors.TemporarilyUnavailable);
        ShouldBeNotAccepted(forAnUnknownMachine);
        ShouldBeNotAccepted(forAnotherAudience);
    }

    [Fact]
    public async Task MissingSigningKey_WithoutBootstrap_IsTemporarilyUnavailable()
    {
        _options.SigningKeyAutoBootstrap = false;

        var result = await _exchange.ExchangeAsync(Request());

        result.IsUnavailable.ShouldBeTrue();
        result.ErrorDescription.ShouldBe(FederatedTokenExchangeService.SigningKeyUnavailableDescription);
        _signingKeys.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task FirstExchange_CreatesTheSigningKey()
    {
        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();
        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();

        _signingKeys.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidRequestShapes_AreInvalidRequests()
    {
        (await _exchange.ExchangeAsync(Request() with { SubjectToken = string.Empty })).ErrorDescription.ShouldBe("The parameter subject_token is missing.");
        (await _exchange.ExchangeAsync(Request() with { MachineId = string.Empty })).ErrorDescription.ShouldBe("The parameter client_id is missing.");
        (await _exchange.ExchangeAsync(Request() with { SubjectTokenType = "urn:ietf:params:oauth:token-type:saml2" })).ErrorDescription.ShouldBe("The subject token type is not supported.");
        (await _exchange.ExchangeAsync(Request() with { RequestedTokenType = OAuthTokenExchange.TokenTypes.IdToken })).ErrorDescription.ShouldBe("The requested token type is not supported.");
        (await _exchange.ExchangeAsync(Request() with { Scopes = ["Default.Read Annotation.Read"] })).ErrorDescription.ShouldBe("The parameter scope is not valid.");

        var result = await _exchange.ExchangeAsync(Request() with { Organization = "org1.other" });
        ShouldBeNotAccepted(result);
        _access.Verify(service => service.GetAccessPointAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(OAuthTokenExchange.TokenTypes.Jwt)]
    [InlineData(OAuthTokenExchange.TokenTypes.IdToken)]
    [InlineData(OAuthTokenExchange.TokenTypes.AccessToken)]
    public async Task EverySubjectTokenType_TakesAJwt(string subjectTokenType)
    {
        (await _exchange.ExchangeAsync(Request() with { SubjectTokenType = subjectTokenType })).Succeeded.ShouldBeTrue();
    }

    private static void ShouldBeNotAccepted(FederatedTokenExchangeResult result)
    {
        result.Succeeded.ShouldBeFalse();
        result.IsUnavailable.ShouldBeFalse();
        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidRequest);
        result.ErrorDescription.ShouldBe(FederatedTokenExchangeResult.NotAcceptedDescription);
    }

    private static Dictionary<string, IReadOnlyList<string>> OwnerPin()
    {
        return new Dictionary<string, IReadOnlyList<string>> { ["repository_owner_id"] = ["4242"] };
    }

    private static MachineAccess Machine()
    {
        return new MachineAccess
        {
            Id = MachineId,
            ObjectId = MachineId,
            AccessKey = string.Empty,
            Scope = MachineAccessScope.DefaultRead,
            AnnotationKey = "unit.service",
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = [new FederationRule { Issuer = "github", Audience = Audience, Subject = "repo:acme/app:*" }],
        };
    }

    private void WithManagementScopes(params MachineManagementScope[] managementScopes)
    {
        _access.Setup(service => service.GetMachineAccessAsync(Organization, Project, MachineId))
            .ReturnsAsync(Machine() with { ManagementScopes = managementScopes });
    }

    private FederatedIssuer GitHubIssuer()
    {
        return new FederatedIssuer
        {
            Name = "github",
            Issuer = GitHub,
            KeySource = FederatedIssuerKeySource.Jwks,
            Jwks = TestSigningKey.Jwks(_key),
            RequiredClaims = OwnerPin(),
        };
    }

    private FederatedTokenExchangeRequest Request(string? subjectToken = null)
    {
        return new FederatedTokenExchangeRequest
        {
            Organization = Organization,
            Project = Project,
            MachineId = MachineId,
            SubjectToken = subjectToken ?? Sign(),
            SubjectTokenType = OAuthTokenExchange.TokenTypes.Jwt,
        };
    }

    private string Sign(
        string subject = "repo:acme/app:ref:refs/heads/main",
        string owner = "4242",
        string audience = Audience,
        string? padding = null,
        string? kid = "")
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var payload = new Dictionary<string, object>
        {
            ["iss"] = GitHub,
            ["aud"] = audience,
            ["sub"] = subject,
            ["repository_owner_id"] = owner,
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 300,
        };

        if (padding is not null)
        {
            payload["padding"] = padding;
        }

        return _key.Sign(payload, kid: kid);
    }

    private void SetPolicy(params MachineCredentialKind[] kinds)
    {
        _access.Setup(service => service.GetAccessPointAsync($"{Organization}.{Project}")).ReturnsAsync(new AccessPoint
        {
            Key = $"{Organization}.{Project}",
            AccessMap = new Dictionary<string, AccountRole>(),
            MachineAuthentication = new MachineAuthenticationPolicy { EnabledCredentialKinds = kinds, CredentialKind = kinds[0] },
        });
    }

    private void SetIssuers(params FederatedIssuer[] issuers)
    {
        _issuers.Setup(store => store.ListAsync(Organization, Project, It.IsAny<CancellationToken>())).ReturnsAsync(issuers);
    }

    private void VerifyNoMachineRead()
    {
        _access.Verify(service => service.GetMachineAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _access.Verify(service => service.GetMachineAccessesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
