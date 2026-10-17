using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class FederationValidationTests
{
    private const string Audience = "urn:storyteller:2s.platform:api";
    private const string GitHubIssuer = "https://token.actions.githubusercontent.com";
    private const string RsaKey = """{"kty":"RSA","kid":"rsa-1","use":"sig","alg":"RS256","n":"0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw","e":"AQAB"}""";
    private const string EcKey = """{"kty":"EC","kid":"ec-1","crv":"P-256","x":"f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU","y":"x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"}""";

    private static readonly FederationOptions Options = new();
    private static readonly FederationOptions PrivateNetworks = new() { AllowPrivateNetworks = true };

    // Issuer: Name
    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("github")]
    [InlineData("k8s-prod-1")]
    public void ValidateIssuer_AcceptsAName(string name)
    {
        FederationValidation.ValidateIssuer(Issuer(name: name), Options).ShouldBeNull();
    }

    [Fact]
    public void ValidateIssuer_AcceptsANameOf63CharactersButNot64()
    {
        FederationValidation.ValidateIssuer(Issuer(name: new string('a', 63)), Options).ShouldBeNull();
        FederationValidation.ValidateIssuer(Issuer(name: new string('a', 64)), Options).ShouldStartWith("The name ");
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Github")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a_b")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("a\n")]
    public void ValidateIssuer_RefusesAName(string name)
    {
        FederationValidation.ValidateIssuer(Issuer(name: name), Options).ShouldStartWith("The name ");
    }

    [Fact]
    public void ValidateIssuer_RefusesAMissingIssuer()
    {
        FederationValidation.ValidateIssuer(null, Options).ShouldBe("The trusted issuer is missing.");
    }

    [Fact]
    public void ValidateIssuer_RefusesAnUnknownKeySource()
    {
        FederationValidation.ValidateIssuer(Issuer() with { KeySource = (FederatedIssuerKeySource)7 }, Options)
            .ShouldBe("The key source 7 doesn't exist.");
    }

    // Issuer: Issuer
    [Fact]
    public void ValidateIssuer_RequiresTheIssuer()
    {
        FederationValidation.ValidateIssuer(Issuer(string.Empty), Options).ShouldBe("The issuer is required.");
    }

    [Fact]
    public void ValidateIssuer_AcceptsAnIssuerOf512CharactersButNot513()
    {
        FederationValidation.ValidateIssuer(JwksIssuer(new string('i', 512)), Options).ShouldBeNull();
        FederationValidation.ValidateIssuer(JwksIssuer(new string('i', 513)), Options).ShouldBe("The issuer can have at most 512 characters.");
    }

    [Theory]
    [InlineData(GitHubIssuer)]
    [InlineData("https://token.actions.githubusercontent.com/acme")]
    [InlineData("https://sts.windows.net/72f988bf-86f1-41af-91ab-2d7cd011db47/")]
    [InlineData("https://oidc.prod-aks.azure.com/00000000-0000-0000-0000-000000000000/11111111-1111-1111-1111-111111111111/")]
    [InlineData("https://example.com:443/issuer")]
    [InlineData("https://xn--bcher-kva.example/")]
    public void ValidateIssuer_Discovery_AcceptsAPublicHttpsUrl(string issuer)
    {
        FederationValidation.ValidateIssuer(Issuer(issuer), Options).ShouldBeNull();
    }

    [Theory]
    [InlineData("http://oidc.example.com", "it must use https.")]
    [InlineData("oidc.example.com", "it must be an absolute URL.")]
    [InlineData("urn:storyteller:issuer", "it must use https.")]
    [InlineData("https://oidc.example.com/?tenant=1", "it must not have a query or a fragment.")]
    [InlineData("https://oidc.example.com/#keys", "it must not have a query or a fragment.")]
    [InlineData("https://oidc.example.com/?", "it must not have a query or a fragment.")]
    [InlineData("https://user:secret@oidc.example.com/", "it must not carry user information.")]
    [InlineData("https://@oidc.example.com/", "it must not carry user information.")]
    [InlineData("https://oidc.example.com:8443/", "it must use port 443.")]
    [InlineData("https://bücher.example/", "its host must be ASCII (use the punycode form).")]
    public void ValidateIssuer_Discovery_RefusesAUrl(string issuer, string reason)
    {
        FederationValidation.ValidateIssuer(Issuer(issuer), Options).ShouldBe($"The issuer {issuer} cannot be discovered: {reason}");
    }

    [Theory]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://2130706433/")]
    [InlineData("https://0x7f.1/")]
    [InlineData("https://127.1/")]
    [InlineData("https://017700000001/")]
    [InlineData("https://0x7f000001/")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    public void ValidateIssuer_Discovery_RefusesIpLiteralsInAnyNotation(string issuer)
    {
        FederationValidation.ValidateIssuer(Issuer(issuer), Options)
            .ShouldBe($"The issuer {issuer} cannot be discovered: its host must be a DNS name, not an IP address.");
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://localhost:5001/realms/dev")]
    [InlineData("https://[::1]:8443/")]
    [InlineData("https://keycloak.internal:8443/")]
    public void ValidateIssuer_PrivateNetworks_LiftHttpsThePortAndTheDnsHost(string issuer)
    {
        FederationValidation.ValidateIssuer(Issuer(issuer), PrivateNetworks).ShouldBeNull();
        FederationValidation.ValidateIssuer(Issuer(issuer), Options).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("https://user@localhost:5001/")]
    [InlineData("ftp://localhost/")]
    [InlineData("http://localhost/?query")]
    public void ValidateIssuer_PrivateNetworks_StillRefuseUserInfoOtherSchemesAndQueries(string issuer)
    {
        FederationValidation.ValidateIssuer(Issuer(issuer), PrivateNetworks).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("urn:k8s:prod")]
    [InlineData("https://kubernetes.default.svc.cluster.local")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("accounts.google.com")]
    public void ValidateIssuer_WithoutDiscovery_AcceptsAnyIssuer(string issuer)
    {
        FederationValidation.ValidateIssuer(JwksIssuer(issuer), Options).ShouldBeNull();
        FederationValidation.ValidateIssuer(JwksUriIssuer(issuer), Options).ShouldBeNull();
    }

    // Issuer: JwksUri
    [Fact]
    public void ValidateIssuer_RequiresTheJwksUriForItsKeySource()
    {
        FederationValidation.ValidateIssuer(JwksUriIssuer() with { JwksUri = null }, Options)
            .ShouldBe("The JWKS URI is required when the key source is JwksUri.");
        FederationValidation.ValidateIssuer(JwksUriIssuer() with { JwksUri = string.Empty }, Options)
            .ShouldBe("The JWKS URI is required when the key source is JwksUri.");
    }

    [Theory]
    [InlineData(FederatedIssuerKeySource.Discovery)]
    [InlineData(FederatedIssuerKeySource.Jwks)]
    public void ValidateIssuer_ForbidsTheJwksUriForOtherKeySources(FederatedIssuerKeySource keySource)
    {
        var issuer = JwksIssuer() with { KeySource = keySource, Issuer = "https://oidc.example.com", Jwks = keySource == FederatedIssuerKeySource.Jwks ? Jwks(RsaKey) : null, JwksUri = "https://oidc.example.com/keys" };

        FederationValidation.ValidateIssuer(issuer, Options).ShouldBe("A JWKS URI is allowed only when the key source is JwksUri.");
    }

    [Theory]
    [InlineData("https://oidc.example.com/openid/v1/jwks")]
    [InlineData("https://oidc.example.com/keys?version=2")]
    public void ValidateIssuer_AcceptsAJwksUri(string jwksUri)
    {
        FederationValidation.ValidateIssuer(JwksUriIssuer() with { JwksUri = jwksUri }, Options).ShouldBeNull();
    }

    [Theory]
    [InlineData("http://oidc.example.com/keys", "it must use https.")]
    [InlineData("https://10.0.0.1/keys", "its host must be a DNS name, not an IP address.")]
    [InlineData("https://0x7f.1/keys", "its host must be a DNS name, not an IP address.")]
    [InlineData("https://oidc.example.com:6443/keys", "it must use port 443.")]
    [InlineData("https://oidc.example.com/keys#a", "it must not have a fragment.")]
    [InlineData("openid/v1/jwks", "it must be an absolute URL.")]
    public void ValidateIssuer_RefusesAJwksUri(string jwksUri, string reason)
    {
        FederationValidation.ValidateIssuer(JwksUriIssuer() with { JwksUri = jwksUri }, Options)
            .ShouldBe($"The JWKS URI {jwksUri} is not valid: {reason}");
    }

    // Issuer: Jwks
    [Fact]
    public void ValidateIssuer_AcceptsAJwksOfRsaAndEcKeys()
    {
        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = Jwks(RsaKey, EcKey) }, Options).ShouldBeNull();
    }

    [Fact]
    public void ValidateIssuer_RequiresTheJwksForItsKeySource()
    {
        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = null }, Options)
            .ShouldBe("The JWKS is required when the key source is Jwks.");
    }

    [Theory]
    [InlineData(FederatedIssuerKeySource.Discovery)]
    [InlineData(FederatedIssuerKeySource.JwksUri)]
    public void ValidateIssuer_ForbidsTheJwksForOtherKeySources(FederatedIssuerKeySource keySource)
    {
        var issuer = Issuer() with { KeySource = keySource, JwksUri = keySource == FederatedIssuerKeySource.JwksUri ? "https://oidc.example.com/keys" : null, Jwks = Jwks(RsaKey) };

        FederationValidation.ValidateIssuer(issuer, Options).ShouldBe("A JWKS is allowed only when the key source is Jwks.");
    }

    [Fact]
    public void ValidateIssuer_RefusesAJwksOver16KiB()
    {
        var padded = Jwks(RsaKey).Replace("\"use\":\"sig\"", $"\"use\":\"sig\",\"x5u\":\"{new string('a', 16 * 1024)}\"");

        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = padded }, Options).ShouldBe("The JWKS is not valid: it can have at most 16 KiB.");
    }

    [Fact]
    public void ValidateIssuer_AcceptsUpTo10KeysButNot11()
    {
        string Keys(int count) => Jwks(Enumerable.Range(1, count).Select(index => EcKey.Replace("ec-1", $"ec-{index}")).ToArray());

        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = Keys(10) }, Options).ShouldBeNull();
        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = Keys(11) }, Options).ShouldBe("The JWKS is not valid: it needs 1 to 10 keys.");
    }

    [Theory]
    [InlineData("not json", "it is not JSON.")]
    [InlineData("[]", "it needs a keys array.")]
    [InlineData("""{"keys":{}}""", "it needs a keys array.")]
    [InlineData("""{"keys":[]}""", "it needs 1 to 10 keys.")]
    [InlineData("""{"keys":["rsa"]}""", "key 1 is not an object.")]
    [InlineData("""{"keys":[{"kty":"RSA","n":"abc","e":"AQAB"}]}""", "key 1 has no kid.")]
    [InlineData("""{"keys":[{"kty":"RSA","kid":"","n":"abc","e":"AQAB"}]}""", "key 1 has no kid.")]
    [InlineData("""{"keys":[{"kty":"RSA","kid":"a","n":"abc","e":"AQAB"},{"kty":"RSA","kid":"a","n":"abc","e":"AQAB"}]}""", "the kid a appears more than once.")]
    [InlineData("""{"keys":[{"kty":"oct","kid":"a","alg":"HS256"}]}""", "the key a is not an RSA or EC key.")]
    [InlineData("""{"keys":[{"kty":"OKP","kid":"a","crv":"Ed25519","x":"abc"}]}""", "the key a is not an RSA or EC key.")]
    [InlineData("""{"keys":[{"kid":"a","n":"abc","e":"AQAB"}]}""", "the key a is not an RSA or EC key.")]
    [InlineData("""{"keys":[{"kty":"RSA","kid":"a","e":"AQAB"}]}""", "the RSA key a needs n and e.")]
    [InlineData("""{"keys":[{"kty":"EC","kid":"a","crv":"secp256k1","x":"abc","y":"def"}]}""", "the EC key a needs crv P-256, P-384 or P-521, x and y.")]
    [InlineData("""{"keys":[{"kty":"EC","kid":"a","crv":"P-384","x":"abc"}]}""", "the EC key a needs crv P-256, P-384 or P-521, x and y.")]
    public void ValidateIssuer_RefusesAJwks(string jwks, string reason)
    {
        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = jwks }, Options).ShouldBe($"The JWKS is not valid: {reason}");
    }

    [Theory]
    [InlineData("d")]
    [InlineData("p")]
    [InlineData("q")]
    [InlineData("dp")]
    [InlineData("dq")]
    [InlineData("qi")]
    [InlineData("k")]
    public void ValidateIssuer_RefusesAJwksWithPrivateKeyMembers(string member)
    {
        var jwks = Jwks(RsaKey.Replace("\"e\":\"AQAB\"", $"\"e\":\"AQAB\",\"{member}\":\"secret\""));

        FederationValidation.ValidateIssuer(JwksIssuer() with { Jwks = jwks }, Options)
            .ShouldBe($"The JWKS is not valid: key 1 has the private member {member}; paste public keys only.");
    }

    // Issuer: MaxTokenLifetimeSeconds, RequiredClaims, Description
    [Theory]
    [InlineData(null)]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(86400)]
    public void ValidateIssuer_AcceptsALifetime(int? seconds)
    {
        FederationValidation.ValidateIssuer(Issuer() with { MaxTokenLifetimeSeconds = seconds }, Options).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(86401)]
    public void ValidateIssuer_RefusesALifetimeOutOfRange(int seconds)
    {
        FederationValidation.ValidateIssuer(Issuer() with { MaxTokenLifetimeSeconds = seconds }, Options)
            .ShouldBe("The maximum token lifetime must be between 60 and 86400 seconds.");
    }

    [Fact]
    public void ValidateIssuer_ChecksRequiredClaimsLikeRuleClaims()
    {
        FederationValidation.ValidateIssuer(Issuer() with { RequiredClaims = Claims(("repository_owner_id", ["1342004"])) }, Options).ShouldBeNull();
        FederationValidation.ValidateIssuer(Issuer() with { RequiredClaims = Claims(("sub", ["x"])) }, Options)
            .ShouldBe("Required claims: the claim sub cannot be pinned; use Subject and Audience.");
        FederationValidation.ValidateIssuer(Issuer() with { RequiredClaims = Claims(("owner", [])) }, Options)
            .ShouldBe("Required claims: the claim owner needs 1 to 10 accepted values.");
    }

    [Fact]
    public void ValidateIssuer_AcceptsADescriptionOf256CharactersButNot257()
    {
        FederationValidation.ValidateIssuer(Issuer() with { Description = new string('d', 256) }, Options).ShouldBeNull();
        FederationValidation.ValidateIssuer(Issuer() with { Description = new string('d', 257) }, Options)
            .ShouldBe("The description can have at most 256 characters.");
    }

    // Issuer changes and the limit per project
    [Fact]
    public void ValidateIssuerChange_KeepsTheIssuerOfAnExistingName()
    {
        var current = new[] { Issuer("https://oidc.example.com") };

        FederationValidation.ValidateIssuerChange(Issuer("https://oidc.example.com") with { Description = "new" }, current).ShouldBeNull();
        FederationValidation.ValidateIssuerChange(Issuer("https://oidc.example.com/"), current)
            .ShouldBe("The issuer of a trusted issuer cannot change; create a new one.");
    }

    [Fact]
    public void ValidateIssuerChange_AllowsAtMost20IssuersPerProject()
    {
        var nineteen = Enumerable.Range(1, 19).Select(index => Issuer(name: $"issuer-{index}")).ToList();
        var twenty = nineteen.Append(Issuer(name: "issuer-20")).ToList();

        FederationValidation.ValidateIssuerChange(Issuer(name: "new"), nineteen).ShouldBeNull();
        FederationValidation.ValidateIssuerChange(Issuer(name: "new"), twenty).ShouldBe("A project has at most 20 trusted issuers.");
        FederationValidation.ValidateIssuerChange(Issuer(name: "issuer-20"), twenty).ShouldBeNull();
    }

    // Rules: count and issuer
    [Fact]
    public void ValidateRules_RequiresOneToTenRules()
    {
        var issuers = new[] { Issuer() };

        FederationValidation.ValidateRules(null, issuers).ShouldBe("A Federated machine needs at least one federation rule.");
        FederationValidation.ValidateRules([], issuers).ShouldBe("A Federated machine needs at least one federation rule.");
        FederationValidation.ValidateRules(Enumerable.Repeat(Rule(), 10).ToList(), issuers).ShouldBeNull();
        FederationValidation.ValidateRules(Enumerable.Repeat(Rule(), 11).ToList(), issuers).ShouldBe("A machine has at most 10 federation rules.");
    }

    [Fact]
    public void ValidateRules_RequiresATrustedIssuerOfTheProject()
    {
        var issuers = new[] { Issuer() };

        FederationValidation.ValidateRules([Rule(issuer: string.Empty)], issuers).ShouldBe("Rule 1: the trusted issuer is required.");
        FederationValidation.ValidateRules([Rule(), Rule(issuer: "gone")], issuers).ShouldBe("Rule 2: the trusted issuer 'gone' doesn't exist in the project.");
        FederationValidation.ValidateRules([Rule(issuer: "CI")], issuers).ShouldBe("Rule 1: the trusted issuer 'CI' doesn't exist in the project.");
        FederationValidation.ValidateRules([null!], issuers).ShouldBe("Rule 1 is missing.");
    }

    [Fact]
    public void ValidateRules_NamesTheRuleAndTheField()
    {
        FederationValidation.ValidateRules([Rule(), Rule(audience: "sts.amazonaws.com")], [Issuer()])
            .ShouldBe("Rule 2: the audience sts.amazonaws.com belongs to another service.");
    }

    [Fact]
    public void ValidateRule_ReturnsASentence()
    {
        FederationValidation.ValidateRule(Rule(audience: "sts.amazonaws.com"), Issuer())
            .ShouldBe("The audience sts.amazonaws.com belongs to another service.");
        FederationValidation.ValidateRule(Rule(issuer: "other"), Issuer())
            .ShouldBe("The rule belongs to the trusted issuer 'other', not 'ci'.");
        FederationValidation.ValidateRule(Rule(), Issuer()).ShouldBeNull();
    }

    // Rules: Audience
    [Fact]
    public void ValidateRule_RequiresAnAudienceOf1To512Characters()
    {
        FederationValidation.ValidateRule(Rule(audience: string.Empty), Issuer()).ShouldBe("The audience is required.");
        FederationValidation.ValidateRule(Rule(audience: new string('a', 512)), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(audience: new string('a', 513)), Issuer()).ShouldBe("The audience can have at most 512 characters.");
    }

    [Theory]
    [InlineData("sts.amazonaws.com")]
    [InlineData("STS.AmazonAWS.com")]
    [InlineData("sts.amazonaws.com/")]
    [InlineData("pods.eks.amazonaws.com")]
    [InlineData("api://AzureADTokenExchange")]
    [InlineData("api://azureadtokenexchange")]
    [InlineData("https://kubernetes.default.svc")]
    [InlineData("https://kubernetes.default.svc.cluster.local")]
    [InlineData("https://kubernetes.default.svc.cluster.local/")]
    [InlineData("//iam.googleapis.com/projects/123/locations/global/workloadIdentityPools/pool/providers/github")]
    [InlineData("https://iam.googleapis.com/projects/123/locations/global/workloadIdentityPools/pool/providers/github")]
    [InlineData("https://github.com/acme")]
    [InlineData("HTTPS://GITHUB.COM/acme")]
    [InlineData("https://vercel.com/acme")]
    [InlineData("https://management.azure.com")]
    [InlineData("https://management.azure.com/")]
    [InlineData("https://management.core.windows.net")]
    [InlineData("https://management.core.windows.net/")]
    [InlineData("https://graph.microsoft.com")]
    [InlineData("https://graph.microsoft.com/")]
    [InlineData("https://vault.azure.net")]
    [InlineData("https://vault.azure.net/")]
    [InlineData("https://storage.azure.com")]
    [InlineData("https://storage.azure.com/")]
    [InlineData("https://database.windows.net")]
    [InlineData("https://database.windows.net/")]
    public void ValidateRule_RefusesTheAudienceOfAnotherService(string audience)
    {
        FederationValidation.ValidateRule(Rule(audience: audience), Issuer())
            .ShouldBe($"The audience {audience} belongs to another service.");
    }

    [Theory]
    [InlineData("https://oidc.example.com", "https://oidc.example.com")]
    [InlineData("https://oidc.example.com", "https://oidc.example.com/")]
    [InlineData("https://oidc.example.com/", "https://oidc.example.com")]
    [InlineData("https://oidc.example.com/", "https://OIDC.example.com/")]
    public void ValidateRule_RefusesTheIssuersOwnIssuerAsTheAudience(string issuer, string audience)
    {
        FederationValidation.ValidateRule(Rule(audience: audience), Issuer(issuer))
            .ShouldBe($"The audience {audience} is the issuer itself, the default audience of its tokens for other services.");
    }

    [Theory]
    [InlineData(Audience)]
    [InlineData("https://storyteller.example.com/api/v1/acme/app/access/token")]
    [InlineData("api://0d7a2bd4-0000-4000-8000-000000000000")]
    [InlineData("https://github.com")]
    [InlineData("sts.amazonaws.com.example")]
    [InlineData("https://oidc.example.com//")]
    public void ValidateRule_AcceptsAStorytellerAudience(string audience)
    {
        FederationValidation.ValidateRule(Rule(audience: audience), Issuer()).ShouldBeNull();
    }

    // Rules: Subject
    [Fact]
    public void ValidateRule_RequiresASubjectOf1To512Characters()
    {
        FederationValidation.ValidateRule(Rule(subject: string.Empty), Issuer()).ShouldBe("The subject is required.");
        FederationValidation.ValidateRule(Rule(subject: new string('s', 512)), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(subject: new string('s', 513)), Issuer()).ShouldBe("The subject can have at most 512 characters.");
    }

    [Theory]
    [InlineData("system:serviceaccount:prod:*")]
    [InlineData("repo:acme/app:*")]
    [InlineData("arn:aws:iam::123456789012:role/ci/*")]
    [InlineData("a/*")]
    public void ValidateRule_AcceptsAWildcardAfterASeparator(string subject)
    {
        FederationValidation.ValidateRule(Rule(subject: subject, claims: Claims(("environment", ["prod"]))), Issuer()).ShouldBeNull();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("system:serviceaccount:prod*")]
    [InlineData("repo:acme/app*")]
    [InlineData("repo:acme/*:ref:*")]
    [InlineData("repo:*/app")]
    [InlineData("repo:acme/app:**")]
    [InlineData("repo-*")]
    public void ValidateRule_RefusesAWildcardThatIsNotSeparatorAnchored(string subject)
    {
        FederationValidation.ValidateRule(Rule(subject: subject, claims: Claims(("environment", ["prod"]))), Issuer())
            .ShouldBe("The subject can contain * only once, as its last character, right after : or /.");
    }

    // Rules: Claims
    [Theory]
    [InlineData("repository")]
    [InlineData("repository_owner_id")]
    [InlineData("kubernetes.io")]
    [InlineData("a:b-c_d.e")]
    [InlineData("SUB")]
    [InlineData("/kubernetes.io/namespace")]
    [InlineData("/kubernetes.io/serviceaccount/uid")]
    [InlineData("/https:~1~1sts.amazonaws.com~1/aws_account")]
    [InlineData("/a~0b")]
    [InlineData("/sub/nested")]
    [InlineData("//")]
    public void ValidateRule_AcceptsAClaimKey(string key)
    {
        FederationValidation.ValidateRule(Rule(claims: Claims((key, ["value"]))), Issuer()).ShouldBeNull();
    }

    [Fact]
    public void ValidateRule_AcceptsClaimNamesUpTo128AndPointersUpTo256Characters()
    {
        FederationValidation.ValidateRule(Rule(claims: Claims((new string('c', 128), ["v"]))), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(claims: Claims(("/" + new string('c', 256), ["v"]))), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(claims: Claims((new string('c', 129), ["v"]))), Issuer()).ShouldStartWith("The claim key ");
        FederationValidation.ValidateRule(Rule(claims: Claims(("/" + new string('c', 257), ["v"]))), Issuer()).ShouldStartWith("The claim key ");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("with space")]
    [InlineData("repösitory")]
    [InlineData("repo/name")]
    [InlineData("/a~2")]
    [InlineData("/a~")]
    [InlineData("/~")]
    [InlineData("/a~b")]
    public void ValidateRule_RefusesAClaimKey(string key)
    {
        FederationValidation.ValidateRule(Rule(claims: Claims((key, ["value"]))), Issuer()).ShouldStartWith($"The claim key '{key}' is neither");
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    [InlineData("aud")]
    [InlineData("exp")]
    [InlineData("nbf")]
    [InlineData("iat")]
    [InlineData("jti")]
    [InlineData("/iss")]
    [InlineData("/sub")]
    [InlineData("/aud")]
    [InlineData("/exp")]
    [InlineData("/nbf")]
    [InlineData("/iat")]
    [InlineData("/jti")]
    public void ValidateRule_RefusesRegisteredClaims(string key)
    {
        FederationValidation.ValidateRule(Rule(claims: Claims((key, ["value"]))), Issuer())
            .ShouldBe($"The claim {key} cannot be pinned; use Subject and Audience.");
    }

    [Fact]
    public void ValidateRule_AcceptsUpTo10ClaimsButNot11()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> Many(int count) => Enumerable.Range(1, count)
            .ToDictionary(index => $"claim{index}", _ => (IReadOnlyList<string>)["value"]);

        FederationValidation.ValidateRule(Rule(claims: Many(10)), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(claims: Many(11)), Issuer()).ShouldBe("At most 10 claims can be pinned.");
    }

    [Fact]
    public void ValidateRule_RequiresOneToTenValuesOf1To512Characters()
    {
        var ten = Enumerable.Range(1, 10).Select(index => $"v{index}").ToArray();
        var eleven = Enumerable.Range(1, 11).Select(index => $"v{index}").ToArray();

        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", ten))), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", [new string('v', 512)]))), Issuer()).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", eleven))), Issuer()).ShouldBe("The claim environment needs 1 to 10 accepted values.");
        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", []))), Issuer()).ShouldBe("The claim environment needs 1 to 10 accepted values.");
        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", null!))), Issuer()).ShouldBe("The claim environment needs 1 to 10 accepted values.");
        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", [string.Empty]))), Issuer())
            .ShouldBe("Each accepted value of the claim environment needs 1 to 512 characters.");
        FederationValidation.ValidateRule(Rule(claims: Claims(("environment", [new string('v', 513)]))), Issuer())
            .ShouldBe("Each accepted value of the claim environment needs 1 to 512 characters.");
    }

    // Name-derived issuers
    [Theory]
    [InlineData(GitHubIssuer)]
    [InlineData("https://token.actions.githubusercontent.com/acme-enterprise")]
    [InlineData("https://token.actions.acme.ghe.com")]
    [InlineData("HTTPS://TOKEN.ACTIONS.GITHUBUSERCONTENT.COM")]
    public void ValidateRule_GitHub_RequiresAnIdPin(string issuer)
    {
        var gitHub = Issuer(issuer);

        FederationValidation.ValidateRule(Rule(subject: "repo:acme/app:ref:refs/heads/main"), gitHub)
            .ShouldStartWith("A rule of a GitHub Actions issuer must pin repository_owner_id, repository_id or enterprise_id");
        FederationValidation.ValidateRule(Rule(subject: "repo:acme/app:ref:refs/heads/main", claims: Claims(("repository", ["acme/app"]))), gitHub)
            .ShouldNotBeNull();
    }

    [Theory]
    [InlineData("repository_owner_id")]
    [InlineData("repository_id")]
    [InlineData("enterprise_id")]
    [InlineData("/repository_owner_id")]
    public void ValidateRule_GitHub_AcceptsAnIdPinInTheRule(string claim)
    {
        FederationValidation.ValidateRule(Rule(subject: "repo:acme/app:*", claims: Claims((claim, ["1342004"]))), Issuer(GitHubIssuer)).ShouldBeNull();
    }

    [Fact]
    public void ValidateRule_GitHub_RequiredClaimsOfTheIssuerSatisfyThePin()
    {
        var gitHub = Issuer(GitHubIssuer) with { RequiredClaims = Claims(("repository_owner_id", ["1342004"])) };

        FederationValidation.ValidateRule(Rule(subject: "repo:acme/app:environment:prod"), gitHub).ShouldBeNull();
        FederationValidation.ValidateRules([Rule(subject: "repo:acme/app:*")], [gitHub]).ShouldBeNull();
    }

    [Fact]
    public void ValidateRule_GitHub_IsDetectedByHostWhateverTheKeySource()
    {
        var gitHub = JwksIssuer(GitHubIssuer);

        FederationValidation.ValidateRule(Rule(issuer: "k8s", subject: "repo:acme/app:ref:refs/heads/main"), gitHub)
            .ShouldStartWith("A rule of a GitHub Actions issuer");
    }

    [Theory]
    [InlineData("https://oidc.vercel.com")]
    [InlineData("https://oidc.vercel.com/acme")]
    public void ValidateRule_Vercel_RequiresAnOwnerIdPin(string issuer)
    {
        var vercel = Issuer(issuer);
        const string subject = "owner:acme:project:web:environment:production";

        FederationValidation.ValidateRule(Rule(subject: subject, claims: Claims(("project_id", ["prj_1"]))), vercel)
            .ShouldStartWith("A rule of a Vercel issuer must pin owner_id");
        FederationValidation.ValidateRule(Rule(subject: subject, claims: Claims(("owner_id", ["team_1"]))), vercel).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(subject: subject), vercel with { RequiredClaims = Claims(("owner_id", ["team_1"])) }).ShouldBeNull();
    }

    // Immutable-id issuers
    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("accounts.google.com")]
    [InlineData("https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47/v2.0")]
    [InlineData("https://sts.windows.net/72f988bf-86f1-41af-91ab-2d7cd011db47/")]
    public void ValidateRule_ImmutableIdIssuers_RefuseASubjectWildcard(string issuer)
    {
        var immutable = JwksIssuer(issuer) with { Name = "ci" };

        FederationValidation.ValidateRule(Rule(subject: "1234567890:*", claims: Claims(("email", ["ci@example.com"]))), immutable)
            .ShouldStartWith("The subject of a Google or Entra issuer cannot use *");
        FederationValidation.ValidateRule(Rule(subject: "110169484474386276334"), immutable).ShouldBeNull();
    }

    // Other issuers
    [Fact]
    public void ValidateRule_OtherIssuers_NeedAPinForASubjectWildcard()
    {
        var eks = Issuer("https://oidc.eks.eu-west-1.amazonaws.com/id/EXAMPLED539D4633E53DE1B71EXAMPLE");

        FederationValidation.ValidateRule(Rule(subject: "system:serviceaccount:prod:*"), eks)
            .ShouldBe("A subject with * needs at least one claim pin, in the rule's claims or in the issuer's required claims.");
        FederationValidation.ValidateRule(Rule(subject: "system:serviceaccount:prod:*", claims: Claims(("/kubernetes.io/namespace", ["prod"]))), eks).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(subject: "system:serviceaccount:prod:*"), eks with { RequiredClaims = Claims(("/kubernetes.io/namespace", ["prod"])) }).ShouldBeNull();
        FederationValidation.ValidateRule(Rule(subject: "system:serviceaccount:prod:app"), eks).ShouldBeNull();
    }

    // Classification
    [Theory]
    [InlineData(GitHubIssuer, SharedIssuerKind.GitHub)]
    [InlineData("https://token.actions.githubusercontent.com/", SharedIssuerKind.GitHub)]
    [InlineData("https://token.actions.githubusercontent.com/acme-enterprise", SharedIssuerKind.GitHub)]
    [InlineData("https://token.actions.acme.ghe.com", SharedIssuerKind.GitHub)]
    [InlineData("https://token.actions.eu.acme.ghe.com", SharedIssuerKind.GitHub)]
    [InlineData("token.actions.githubusercontent.com", SharedIssuerKind.GitHub)]
    [InlineData("https://token.actions.githubusercontent.com./", SharedIssuerKind.GitHub)]
    [InlineData("https://token.actions.ghe.com", SharedIssuerKind.None)]
    [InlineData("https://token.actions.githubusercontent.com.example.com", SharedIssuerKind.None)]
    [InlineData("https://oidc.vercel.com", SharedIssuerKind.Vercel)]
    [InlineData("https://oidc.vercel.com/acme", SharedIssuerKind.Vercel)]
    [InlineData("https://accounts.google.com", SharedIssuerKind.Google)]
    [InlineData("accounts.google.com", SharedIssuerKind.Google)]
    [InlineData("https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47/v2.0", SharedIssuerKind.Entra)]
    [InlineData("https://sts.windows.net/72f988bf-86f1-41af-91ab-2d7cd011db47/", SharedIssuerKind.Entra)]
    [InlineData("https://oidc.prod-aks.azure.com/tenant/issuer/", SharedIssuerKind.None)]
    [InlineData("https://kubernetes.default.svc.cluster.local", SharedIssuerKind.None)]
    [InlineData("urn:k8s:prod", SharedIssuerKind.None)]
    [InlineData("", SharedIssuerKind.None)]
    public void ClassifyIssuer_DetectsSharedIssuersByHost(string issuer, SharedIssuerKind expected)
    {
        FederationValidation.ClassifyIssuer(issuer).ShouldBe(expected);
    }

    // Claim keys
    [Theory]
    [InlineData("repository", new[] { "repository" })]
    [InlineData("/kubernetes.io/namespace", new[] { "kubernetes.io", "namespace" })]
    [InlineData("/https:~1~1sts.amazonaws.com~1/aws_account", new[] { "https://sts.amazonaws.com/", "aws_account" })]
    [InlineData("/a~1b/c~0d", new[] { "a/b", "c~d" })]
    [InlineData("/~01", new[] { "~1" })]
    [InlineData("/~10", new[] { "/0" })]
    [InlineData("//", new[] { "", "" })]
    public void ClaimKeys_DecodeAPath(string key, string[] expected)
    {
        FederationClaimKeys.TryGetPath(key, out var path).ShouldBeTrue();
        path.ShouldBe(expected);
    }

    [Theory]
    [InlineData("/a~2")]
    [InlineData("/a~")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a b")]
    public void ClaimKeys_RefuseAnInvalidKey(string? key)
    {
        FederationClaimKeys.TryGetPath(key, out _).ShouldBeFalse();
    }

    [Fact]
    public void ClaimKeys_RecognizeATopLevelClaimBareOrAsPointer()
    {
        FederationClaimKeys.IsTopLevel("owner_id", "owner_id").ShouldBeTrue();
        FederationClaimKeys.IsTopLevel("/owner_id", "owner_id").ShouldBeTrue();
        FederationClaimKeys.IsTopLevel("/owner/owner_id", "owner_id").ShouldBeFalse();
        FederationClaimKeys.IsTopLevel("Owner_id", "owner_id").ShouldBeFalse();
    }

    private static FederatedIssuer Issuer(string issuer = "https://oidc.example.com", string name = "ci")
    {
        return new FederatedIssuer { Name = name, Issuer = issuer };
    }

    private static FederatedIssuer JwksIssuer(string issuer = "https://kubernetes.default.svc.cluster.local")
    {
        return new FederatedIssuer { Name = "k8s", Issuer = issuer, KeySource = FederatedIssuerKeySource.Jwks, Jwks = Jwks(RsaKey) };
    }

    private static FederatedIssuer JwksUriIssuer(string issuer = "https://kubernetes.default.svc.cluster.local")
    {
        return new FederatedIssuer { Name = "k8s", Issuer = issuer, KeySource = FederatedIssuerKeySource.JwksUri, JwksUri = "https://oidc.example.com/openid/v1/jwks" };
    }

    private static string Jwks(params string[] keys)
    {
        return $$"""{"keys":[{{string.Join(',', keys)}}]}""";
    }

    private static FederationRule Rule(
        string issuer = "ci",
        string audience = Audience,
        string subject = "system:serviceaccount:prod:app",
        IReadOnlyDictionary<string, IReadOnlyList<string>>? claims = null)
    {
        return new FederationRule { Issuer = issuer, Audience = audience, Subject = subject, Claims = claims };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Claims(params (string Key, string[] Values)[] pins)
    {
        return pins.ToDictionary(pin => pin.Key, pin => (IReadOnlyList<string>)pin.Values, StringComparer.Ordinal);
    }
}
