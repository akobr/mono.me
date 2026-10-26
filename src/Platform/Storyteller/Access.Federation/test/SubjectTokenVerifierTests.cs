using System.Text;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class SubjectTokenVerifierTests : IDisposable
{
    private const string Organization = "org1";
    private const string Project = "proj1";
    private const string IssuerUrl = "https://cluster.example";
    private const string Audience = "urn:test:storyteller";
    private const string Subject = "system:serviceaccount:prod:app";

    private readonly ManualTimeProvider _time = new();
    private readonly StubHttpMessageHandler _http = new();
    private readonly RecordingLogger<SubjectTokenVerifier> _logger = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:test", Audience = "urn:storyteller:test:api" };
    private readonly TestSigningKey _rsa = TestSigningKey.Rsa("rsa-1");
    private readonly TestSigningKey _ec = TestSigningKey.EllipticCurve("ec-1");

    // Shared by the verifiers of one test, so fetched keys stay cached between its calls.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _rsa.Dispose();
        _ec.Dispose();
        _http.Dispose();
        _cache.Dispose();
    }

    [Theory]
    [InlineData(SecurityAlgorithms.RsaSha256)]
    [InlineData(SecurityAlgorithms.RsaSha512)]
    [InlineData(SecurityAlgorithms.RsaSsaPssSha256)]
    public async Task RsaToken_MatchingTheRule_IsAccepted(string algorithm)
    {
        var match = await VerifyAsync(_rsa.Sign(Payload(), algorithm), Machine(Rule()), Issuer());

        match.ShouldNotBeNull();
        match.Issuer.Name.ShouldBe("cluster");
        match.RuleNumber.ShouldBe(1);
        match.Subject.ShouldBe(Subject);
    }

    [Fact]
    public async Task EcToken_MatchingTheRule_IsAccepted()
    {
        (await VerifyAsync(_ec.Sign(Payload()), Machine(Rule()), Issuer())).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("123456789012", true)]
    [InlineData("210987654321", false)]
    public async Task Es384TokenOfAwsSts_MatchesItsNestedAccountByPointer(string pinnedAccount, bool matches)
    {
        // AWS STS signs with ES384 on P-384 and nests its claims in the object "https://sts.amazonaws.com/".
        const string role = "arn:aws:iam::123456789012:role/billing-sync";
        using var aws = TestSigningKey.EllipticCurveP384("aws-1");
        var payload = Payload(role);
        payload["https://sts.amazonaws.com/"] = new Dictionary<string, object> { ["aws_account"] = "123456789012" };
        var token = aws.Sign(payload);
        var rule = Rule() with { Subject = role, Claims = Pins("/https:~1~1sts.amazonaws.com~1/aws_account", pinnedAccount) };

        new JsonWebToken(token).Alg.ShouldBe(SecurityAlgorithms.EcdsaSha384);
        var match = await VerifyAsync(token, Machine(rule), Issuer(keys: [aws]));

        (match is not null).ShouldBe(matches);
    }

    [Theory]
    [InlineData("https://cluster.example/", IssuerUrl)]
    [InlineData(IssuerUrl, "https://cluster.example/")]
    [InlineData("https://CLUSTER.example", IssuerUrl)]
    public async Task Issuer_IsComparedExactly(string tokenIssuer, string trustedIssuer)
    {
        var payload = Payload();
        payload["iss"] = tokenIssuer;

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer(issuer: trustedIssuer))).ShouldBeNull();
    }

    [Fact]
    public async Task UntrustedIssuer_IsRefusedWithoutAFetch_AndLoggedAtDebug()
    {
        var discovered = new FederatedIssuer { Name = "cluster", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.Discovery };
        var payload = Payload();
        payload["iss"] = "https://other.example";

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), discovered)).ShouldBeNull();

        _http.TotalRequests.ShouldBe(0);
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("not trusted"));
        _logger.Entries.ShouldNotContain(entry => entry.Level > LogLevel.Debug);
    }

    [Fact]
    public async Task TwoIssuersWithOneIssuerString_ATokenOfOneNeverMatchesTheRuleOfTheOther()
    {
        // Two self-managed clusters keep the default issuer and even use the same kid.
        using var keyA = TestSigningKey.Rsa("cluster-key");
        using var keyB = TestSigningKey.Rsa("cluster-key");
        var issuerA = Issuer("cluster-a", keys: [keyA]);
        var issuerB = Issuer("cluster-b", keys: [keyB]);
        var token = keyB.Sign(Payload());

        (await VerifyAsync(token, Machine(Rule("cluster-a")), issuerA, issuerB)).ShouldBeNull();

        var match = await VerifyAsync(token, Machine(Rule("cluster-a"), Rule("cluster-b")), issuerA, issuerB);
        match.ShouldNotBeNull();
        match.Issuer.Name.ShouldBe("cluster-b");
        match.RuleNumber.ShouldBe(2);
    }

    [Fact]
    public async Task Audience_AsAStringOrAOneElementArray_IsAccepted()
    {
        var single = Payload();
        single["aud"] = new[] { Audience };

        (await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule()), Issuer())).ShouldNotBeNull();
        (await VerifyAsync(_rsa.Sign(single), Machine(Rule()), Issuer())).ShouldNotBeNull();
    }

    [Fact]
    public async Task TokenForSeveralAudiences_IsRefused()
    {
        var payload = Payload();
        payload["aud"] = new[] { Audience, "https://other-relying-party.example" };

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task OtherAudience_IsRefused()
    {
        var payload = Payload();
        payload["aud"] = "urn:test:other";

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Theory]
    [InlineData(Subject, Subject, true)]
    [InlineData(Subject, "system:serviceaccount:prod:other", false)]
    [InlineData("system:serviceaccount:prod:*", Subject, true)]
    [InlineData("system:serviceaccount:prod:*", "system:serviceaccount:production:app", false)]
    [InlineData("arn:aws:iam::123456789012:role/ci/*", "arn:aws:iam::123456789012:role/ci/deploy", true)]
    [InlineData("arn:aws:iam::123456789012:role/ci/*", "arn:aws:iam::123456789012:role/cifoo", false)]
    public async Task Subject_MatchesExactlyOrBySeparatorAnchoredPrefix(string pattern, string subject, bool matches)
    {
        var payload = Payload(subject);
        payload["team"] = "platform";
        var rule = Rule() with { Subject = pattern, Claims = Pins("team", "platform") };

        var match = await VerifyAsync(_rsa.Sign(payload), Machine(rule), Issuer());

        (match is not null).ShouldBe(matches);
    }

    [Theory]
    [InlineData("environment", "production", true)]
    [InlineData("environment", "Production", false)]
    [InlineData("run_number", "42", true)]
    [InlineData("run_number", "42.0", false)]
    [InlineData("protected", "true", true)]
    [InlineData("groups", "deployers", true)]
    [InlineData("groups", "admins", false)]
    [InlineData("/kubernetes.io/namespace", "prod", true)]
    [InlineData("/kubernetes.io/serviceaccount/uid", "0b5c", true)]
    [InlineData("/https:~1~1sts.amazonaws.com~1/aws_account", "123456789012", true)]
    [InlineData("/groups/1", "deployers", true)]
    [InlineData("kubernetes.io", "prod", false)]
    [InlineData("nothing", "null", false)]
    [InlineData("missing", "x", false)]
    public async Task Claims_MatchStringsNumbersBooleansArraysAndPointers(string key, string accepted, bool matches)
    {
        var payload = Payload();
        payload["environment"] = "production";
        payload["run_number"] = 42;
        payload["protected"] = true;
        payload["groups"] = new[] { "readers", "deployers" };
        payload["nothing"] = null;
        payload["kubernetes.io"] = new Dictionary<string, object>
        {
            ["namespace"] = "prod",
            ["serviceaccount"] = new Dictionary<string, object> { ["name"] = "app", ["uid"] = "0b5c" },
        };
        payload["https://sts.amazonaws.com/"] = new Dictionary<string, object> { ["aws_account"] = "123456789012" };
        var rule = Rule() with { Claims = Pins(key, accepted) };

        var match = await VerifyAsync(_rsa.Sign(payload), Machine(rule), Issuer());

        (match is not null).ShouldBe(matches);
    }

    [Fact]
    public async Task EveryPinnedClaimMustMatch_AnyAcceptedValueWill()
    {
        var payload = Payload();
        payload["environment"] = "staging";
        payload["team"] = "platform";
        var rule = Rule() with
        {
            Claims = new Dictionary<string, IReadOnlyList<string>>
            {
                ["environment"] = ["production", "staging"],
                ["team"] = ["platform"],
            },
        };
        var stricter = rule with
        {
            Claims = new Dictionary<string, IReadOnlyList<string>>(rule.Claims!) { ["team"] = ["data"] },
        };

        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), Issuer())).ShouldNotBeNull();
        (await VerifyAsync(_rsa.Sign(payload), Machine(stricter), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task RequiredClaimsOfTheIssuer_AreAndedIntoEveryRule()
    {
        var issuer = Issuer() with { RequiredClaims = Pins("tenant", "acme") };
        var withTenant = Payload();
        withTenant["tenant"] = "acme";
        var otherTenant = Payload();
        otherTenant["tenant"] = "globex";

        (await VerifyAsync(_rsa.Sign(withTenant), Machine(Rule()), issuer)).ShouldNotBeNull();
        (await VerifyAsync(_rsa.Sign(otherTenant), Machine(Rule()), issuer)).ShouldBeNull();
        (await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule()), issuer)).ShouldBeNull();
    }

    [Fact]
    public async Task GitHubRuleWithoutAnIdPin_IsSkipped_AndWithTheIssuersPin_Matches()
    {
        const string gitHub = "https://token.actions.githubusercontent.com";
        var payload = Payload("repo:acme/app:ref:refs/heads/main");
        payload["iss"] = gitHub;
        payload["repository_owner_id"] = "4242";
        var rule = Rule("github") with { Subject = "repo:acme/app:*" };
        var pinned = Issuer("github", issuer: gitHub) with { RequiredClaims = Pins("repository_owner_id", "4242") };

        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), pinned)).ShouldNotBeNull();

        // An administrator dropped the pin; the rule written under it stops matching.
        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), pinned with { RequiredClaims = null })).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("rule 1 is skipped"));
    }

    [Fact]
    public async Task WildcardRule_LeftWithoutAPinByAnIssuerChange_IsSkipped()
    {
        var payload = Payload();
        payload["team"] = "platform";
        var rule = Rule() with { Subject = "system:serviceaccount:prod:*" };
        var pinned = Issuer() with { RequiredClaims = Pins("team", "platform") };

        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), pinned)).ShouldNotBeNull();
        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), pinned with { RequiredClaims = null })).ShouldBeNull();
    }

    [Fact]
    public async Task TokenLongerThanTheIssuersLimit_IsRefused()
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var payload = Payload();
        payload["exp"] = now + 7200;

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer())).ShouldBeNull();
        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer() with { MaxTokenLifetimeSeconds = 7200 })).ShouldNotBeNull();
    }

    [Fact]
    public async Task ExpiredToken_IsRefused()
    {
        var token = _rsa.Sign(Payload());
        _time.Advance(TimeSpan.FromMinutes(11));

        (await VerifyAsync(token, Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task TokenIssuedInTheFuture_IsRefused()
    {
        var payload = Payload();
        payload["iat"] = _time.GetUtcNow().ToUnixTimeSeconds() + 120;

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Theory]
    [InlineData("iat")]
    [InlineData("sub")]
    [InlineData("exp")]
    public async Task TokenWithoutARequiredClaim_IsRefused(string claim)
    {
        var payload = Payload();
        payload.Remove(claim);

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task NumericSubject_IsRefused()
    {
        var payload = Payload();
        payload["sub"] = 42;

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule() with { Subject = "42" }), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithoutKid_IsRefused()
    {
        (await VerifyAsync(_rsa.Sign(Payload(), kid: null), Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithoutKid_OfAFetchedKeySet_IsRefused_NeitherUnavailableNorFetchedAgain()
    {
        const string jwksUrl = "https://cluster.example/openid/v1/jwks";
        var discovered = new FederatedIssuer { Name = "cluster", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.Discovery };
        _http.SetJson($"{IssuerUrl}/.well-known/openid-configuration", System.Text.Json.JsonSerializer.Serialize(new { issuer = IssuerUrl, jwks_uri = jwksUrl }));
        _http.SetJson(jwksUrl, TestSigningKey.Jwks(_rsa));
        (await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule()), discovered)).ShouldNotBeNull();

        // An unknown kid would answer 503 within the minute of the fetch and fetch again after it; no kid does neither.
        (await VerifyAsync(_rsa.Sign(Payload(), kid: null), Machine(Rule()), discovered)).ShouldBeNull();
        _time.Advance(TimeSpan.FromMinutes(2));
        (await VerifyAsync(_rsa.Sign(Payload(), kid: null), Machine(Rule()), discovered)).ShouldBeNull();

        _http.Count(jwksUrl).ShouldBe(1);
        _logger.Entries.Count(entry => entry.Message.EndsWith("the token has no kid", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task TokenWithAnUnknownKid_OfAStoredKeySet_IsRefusedWithoutAFetch()
    {
        (await VerifyAsync(_rsa.Sign(Payload(), kid: "rotated"), Machine(Rule()), Issuer())).ShouldBeNull();
        _http.TotalRequests.ShouldBe(0);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRefused()
    {
        using var other = TestSigningKey.Rsa("rsa-1");

        (await VerifyAsync(other.Sign(Payload()), Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task AlgNone_IsRefused()
    {
        var token = TestSigningKey.Unsigned(new { alg = "none", typ = "JWT", kid = _rsa.Kid }, Payload());

        (await VerifyAsync(token, Machine(Rule()), Issuer())).ShouldBeNull();
        (await VerifyAsync(token + "c2lnbmF0dXJl", Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task Hs256_IsRefused()
    {
        // Signed with the issuer's public key as the HMAC secret, the classic algorithm confusion.
        var publicJwks = TestSigningKey.Jwks(_rsa);
        var secret = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(publicJwks.PadRight(64, '.'))) { KeyId = _rsa.Kid };
        var token = new JsonWebTokenHandler().CreateToken(
            System.Text.Json.JsonSerializer.Serialize(Payload()),
            new SigningCredentials(secret, SecurityAlgorithms.HmacSha256));

        (await VerifyAsync(token, Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task OversizedToken_IsRefusedBeforeParsing()
    {
        _options.MaxSubjectTokenBytes = 1024;
        var payload = Payload();
        payload["padding"] = new string('x', 1024);

        (await VerifyAsync(_rsa.Sign(payload), Machine(Rule()), Issuer())).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("more than 1024 bytes"));
    }

    [Theory]
    [InlineData("not a token")]
    [InlineData("a.b.c.d.e")]
    [InlineData("")]
    public async Task MalformedToken_IsRefused(string token)
    {
        (await VerifyAsync(token, Machine(Rule()), Issuer())).ShouldBeNull();
    }

    [Fact]
    public async Task UnreachableKeys_AreUnavailable()
    {
        var discovered = new FederatedIssuer { Name = "cluster", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.Discovery };
        _http.SetJson($"{IssuerUrl}/.well-known/openid-configuration", "{}", System.Net.HttpStatusCode.InternalServerError);

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => VerifyAsync(_rsa.Sign(Payload()), Machine(Rule()), discovered));
    }

    [Fact]
    public async Task UnreachableKeysOfOneIssuer_DoNotStopAnotherThatVerifiesTheToken()
    {
        var unreachable = new FederatedIssuer { Name = "cluster-a", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.Discovery };
        _http.SetJson($"{IssuerUrl}/.well-known/openid-configuration", "{}", System.Net.HttpStatusCode.ServiceUnavailable);

        var match = await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule("cluster-a"), Rule("cluster-b")), unreachable, Issuer("cluster-b"));

        match.ShouldNotBeNull();
        match.Issuer.Name.ShouldBe("cluster-b");
    }

    [Fact]
    public async Task TokenWithANewKid_IsUnavailableWithinAMinuteOfTheFetch_AndAcceptedAfterIt()
    {
        const string jwksUrl = "https://cluster.example/openid/v1/jwks";
        var discovered = new FederatedIssuer { Name = "cluster", Issuer = IssuerUrl, KeySource = FederatedIssuerKeySource.Discovery };
        _http.SetJson($"{IssuerUrl}/.well-known/openid-configuration", System.Text.Json.JsonSerializer.Serialize(new { issuer = IssuerUrl, jwks_uri = jwksUrl }));
        _http.SetJson(jwksUrl, TestSigningKey.Jwks(_rsa));
        (await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule()), discovered)).ShouldNotBeNull();

        // The cluster rotated its key after the fetch.
        using var rotated = TestSigningKey.Rsa("rsa-2");
        _http.SetJson(jwksUrl, TestSigningKey.Jwks(_rsa, rotated));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => VerifyAsync(rotated.Sign(Payload()), Machine(Rule()), discovered));
        _http.Count(jwksUrl).ShouldBe(1);

        _time.Advance(TimeSpan.FromMinutes(1));
        (await VerifyAsync(rotated.Sign(Payload()), Machine(Rule()), discovered)).ShouldNotBeNull();
        _http.Count(jwksUrl).ShouldBe(2);

        // A kid the issuer still doesn't publish after the fetch may be published soon: retry later.
        _time.Advance(TimeSpan.FromMinutes(1));
        await Should.ThrowAsync<BearerKeyRetrievalException>(() => VerifyAsync(_rsa.Sign(Payload(), kid: "rsa-3"), Machine(Rule()), discovered));
        _http.Count(jwksUrl).ShouldBe(3);
    }

    [Fact]
    public async Task RuleOfAMissingIssuer_IsIgnored()
    {
        (await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule("deleted")), Issuer())).ShouldBeNull();
        (await VerifyAsync(_rsa.Sign(Payload()), Machine(Rule("deleted"), Rule()), Issuer())).ShouldNotBeNull();
    }

    // A trusted issuer stored before Storyteller's own issuer was refused, with its published keys: a user token of the
    // exchange (act, restrictions) never becomes a machine token.
    [Theory]
    [InlineData("urn:storyteller:test")]
    [InlineData("urn:storyteller:test/")]
    public async Task StorytellerToken_IsRefusedEvenByATrustedIssuerOfStoryteller(string issuer)
    {
        var payload = Payload("user_01");
        payload["iss"] = issuer;
        payload["aud"] = _options.Audience;
        payload["token_use"] = "user";
        payload["act"] = new Dictionary<string, object> { ["sub"] = "mcp" };
        payload["restrictions"] = new[] { ActorRestrictions.NoSecretReveal };
        var rule = new FederationRule { Issuer = "storyteller", Audience = _options.Audience, Subject = "user_01" };

        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), Issuer("storyteller", issuer))).ShouldBeNull();

        _logger.Entries.ShouldContain(entry => entry.Message.Contains("the token is a Storyteller token"));

        // The same token of another issuer matches, so only the issuer refuses it.
        payload["iss"] = "urn:storyteller:copy";
        (await VerifyAsync(_rsa.Sign(payload), Machine(rule), Issuer("storyteller", "urn:storyteller:copy"))).ShouldNotBeNull();
    }

    private static MachineAccess Machine(params FederationRule[] rules)
    {
        var id = Guid.NewGuid().ToString("D");
        return new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = string.Empty,
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            CredentialKind = MachineCredentialKind.Federated,
            FederationRules = rules,
        };
    }

    private static FederationRule Rule(string issuer = "cluster")
    {
        return new FederationRule { Issuer = issuer, Audience = Audience, Subject = Subject };
    }

    private static Dictionary<string, IReadOnlyList<string>> Pins(string key, params string[] values)
    {
        return new Dictionary<string, IReadOnlyList<string>> { [key] = values };
    }

    private FederatedIssuer Issuer(string name = "cluster", string issuer = IssuerUrl, params TestSigningKey[] keys)
    {
        return new FederatedIssuer
        {
            Name = name,
            Issuer = issuer,
            KeySource = FederatedIssuerKeySource.Jwks,
            Jwks = TestSigningKey.Jwks(keys.Length > 0 ? keys : [_rsa, _ec]),
        };
    }

    private Dictionary<string, object?> Payload(string subject = Subject)
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        return new Dictionary<string, object?>
        {
            ["iss"] = IssuerUrl,
            ["aud"] = Audience,
            ["sub"] = subject,
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 600,
        };
    }

    private Task<SubjectTokenMatch?> VerifyAsync(string token, MachineAccess machine, params FederatedIssuer[] issuers)
    {
        var options = Options.Create(_options);
        var resolver = new FederatedIssuerKeyResolver(
            new StubHttpClientFactory(_http),
            _cache,
            options,
            _time,
            NullLogger<FederatedIssuerKeyResolver>.Instance);

        return new SubjectTokenVerifier(resolver, options, _time, _logger)
            .VerifyAsync(token, machine, issuers, Organization, Project);
    }
}
