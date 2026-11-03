using _42.Platform.Storyteller.Sdk.Credentials;
using Aspire.Hosting;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

// The package copies the SDK's variable names, the rules of FullKey and the SDK's URL rule, so an AppHost does not
// reference them; these tests keep the copies equal to the originals.
public sealed class ContractTests
{
    // Credential names of the SDK that no resource sets: processes started by an AppHost have no projected or CI token,
    // and the canonical names are read before the deprecated ones.
    private static readonly string[] NotOffered =
    [
        StorytellerVariables.TOKEN_FILE,
        StorytellerVariables.SUBJECT_TOKEN_TYPE,
        StorytellerVariables.SUBJECT_TOKEN_FILE,
        StorytellerVariables.CERTIFICATE_FILE,
        StorytellerVariables.CERTIFICATE_PASSWORD,
    ];

    [Fact]
    public void TheEndpointAndKeyNames_AreTheOnesOfTheSdk()
    {
        StorytellerVariableNames.ENDPOINT.ShouldBe(StorytellerVariables.ENDPOINT);
        StorytellerVariableNames.API_URL.ShouldBe(StorytellerVariables.API_URL);
        StorytellerVariableNames.KEY.ShouldBe(StorytellerVariables.KEY);
    }

    // Every name the SDK reads is masked, so a new SDK credential name fails here until it is copied.
    [Fact]
    public void TheCredentialNames_AreTheOnesOfTheSdk()
    {
        StorytellerVariableNames.Credentials.ShouldBe(StorytellerVariables.Credentials, ignoreOrder: true);
        StorytellerVariables.Deprecated.Keys.ShouldBeSubsetOf(StorytellerVariableNames.Identity);
    }

    [Fact]
    public void TheNamedCredentialConstants_AreTheOnesOfTheSdk()
    {
        StorytellerVariableNames.API_KEY.ShouldBe(StorytellerVariables.API_KEY);
        StorytellerVariableNames.CLIENT_ID.ShouldBe(StorytellerVariables.CLIENT_ID);
        StorytellerVariableNames.CLIENT_SECRET.ShouldBe(StorytellerVariables.CLIENT_SECRET);
        StorytellerVariableNames.TOKEN_URL.ShouldBe(StorytellerVariables.TOKEN_URL);
        StorytellerVariableNames.TOKEN_SCOPE.ShouldBe(StorytellerVariables.TOKEN_SCOPE);
        StorytellerVariableNames.CLIENT_CERTIFICATE.ShouldBe(StorytellerVariables.CLIENT_CERTIFICATE);
        StorytellerVariableNames.CLIENT_CERTIFICATE_PASSWORD.ShouldBe(StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD);
        StorytellerVariableNames.CERTIFICATE_FILE.ShouldBe(StorytellerVariables.CERTIFICATE_FILE);
        StorytellerVariableNames.CERTIFICATE_PASSWORD.ShouldBe(StorytellerVariables.CERTIFICATE_PASSWORD);
        StorytellerVariableNames.TOKEN_FILE.ShouldBe(StorytellerVariables.TOKEN_FILE);
        StorytellerVariableNames.SUBJECT_TOKEN_FILE.ShouldBe(StorytellerVariables.SUBJECT_TOKEN_FILE);
        StorytellerVariableNames.SUBJECT_TOKEN_TYPE.ShouldBe(StorytellerVariables.SUBJECT_TOKEN_TYPE);
    }

    // What the resources write with every credential set, so a new SDK credential name fails here until a With… method
    // sets it or it is listed as not offered.
    [Fact]
    public void TheNamesResourcesSet_AreTheSdkCredentials_MinusThoseNotOffered()
    {
        var host = TestAppHost.Run();
        var withCertificate = host.Builder.AddStoryteller("with-certificate", "https://storyteller.example.com/api")
            .WithApiKey(host.Builder.AddParameter("storyteller-api-key", secret: true))
            .WithClientCertificate("machine.pfx", host.Builder.AddParameter("storyteller-certificate-password", secret: true));
        var withClientCredentials = host.Builder.AddStoryteller("with-client-credentials", "https://storyteller.example.com/api")
            .WithClientCredentials(
                host.Builder.AddParameter("storyteller-client-id"),
                host.Builder.AddParameter("storyteller-client-secret", secret: true),
                "https://login.example.com/oauth2/token",
                "api://storyteller/.default");

        var written = new[] { withCertificate.Resource, withClientCredentials.Resource }
            .SelectMany(resource =>
            {
                var variables = new Dictionary<string, object>();
                resource.WriteVariables(variables, maskUnset: false);
                return variables.Keys;
            })
            .Where(name => name != StorytellerVariables.ENDPOINT)
            .Distinct();

        written.ShouldBe(StorytellerVariables.Credentials.Except(NotOffered), ignoreOrder: true);
        NotOffered.ShouldBeSubsetOf(StorytellerVariables.Credentials);
    }

    // The rest of the names, empty, on top of what the resource sets.
    [Fact]
    public void AMaskingWrite_WritesEveryNameTheSdkReads()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");
        var variables = new Dictionary<string, object>();

        storyteller.Resource.WriteVariables(variables, maskUnset: true);

        variables.Keys.ShouldBe([StorytellerVariables.ENDPOINT, StorytellerVariables.API_URL, StorytellerVariables.KEY, .. StorytellerVariables.Credentials], ignoreOrder: true);
        variables.Where(variable => !Equals(variable.Value, string.Empty)).Select(variable => variable.Key).ShouldBe([StorytellerVariables.ENDPOINT]);
    }

    [Theory]
    [InlineData("@acme.billing.default.rst.invoicing")]
    [InlineData("@acme.billing.default.sbt.northwind")]
    [InlineData("@acme.billing.default.usg.northwind.invoicing")]
    [InlineData("@acme.billing.default.cnt.northwind.dev")]
    [InlineData("@acme.billing.default.unt.invoicing.nightly")]
    [InlineData("@acme.billing.default.exe.northwind.invoicing.dev")]
    [InlineData("@acme.billing.default.uxe.northwind.invoicing.dev.nightly")]
    [InlineData("@acme.billing.default.EXE.northwind.invoicing.dev")]
    [InlineData("@a b.billing.default.rst.invoicing")]
    [InlineData("@@acme.billing.default.rst.invoicing")]
    [InlineData("@acme.billing.default.rst")]
    [InlineData("@acme.billing.default.rst.invoicing.extra")]
    [InlineData("@acme.billing.default.sbt.northwind.extra")]
    [InlineData("@acme.billing.default.usg.northwind")]
    [InlineData("@acme.billing.default.cnt.northwind.dev.extra")]
    [InlineData("@acme.billing.default.unt.invoicing")]
    [InlineData("@acme.billing.default.exe.northwind.invoicing")]
    [InlineData("@acme.billing.default.exe.northwind.invoicing.dev.nightly")]
    [InlineData("@acme.billing.default.uxe.northwind.invoicing.dev")]
    [InlineData("@acme.billing.default.uxe.northwind.invoicing.dev.nightly.extra")]
    [InlineData("@acme.billing.default.xyz.northwind")]
    [InlineData("@acme.billing.rst.invoicing")]
    [InlineData("@acme.billing.default..invoicing")]
    [InlineData("@acme.billing.default.rst. ")]
    [InlineData("@.billing.default.rst.invoicing")]
    [InlineData("@acme.billing.default.rst.invoicing.")]
    [InlineData("acme.billing.default.rst.invoicing")]
    [InlineData("rst.invoicing")]
    [InlineData("@")]
    [InlineData(" @acme.billing.default.rst.invoicing")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void TheKeyRule_IsTheOneOfFullKey(string? key)
    {
        StorytellerKeyRule.IsFullKey(key).ShouldBe(FullKey.TryParse(key!, out _));
    }

    [Theory]
    [InlineData("rst.invoicing", true)]
    [InlineData("exe.northwind.invoicing.dev", true)]
    [InlineData("uxe.northwind.invoicing.dev.nightly", true)]
    [InlineData("exe.northwind.invoicing", false)]
    [InlineData("@acme.billing.default.rst.invoicing", false)]
    [InlineData("rst.", false)]
    [InlineData("", false)]
    public void AnAnnotationKey_IsCheckedAsTheFullKeyItBecomes(string key, bool isValid)
    {
        StorytellerKeyRule.IsAnnotationKey(key).ShouldBe(isValid);

        if (!key.StartsWith('@'))
        {
            FullKey.TryParse($"@acme.billing.default.{key}", out _).ShouldBe(isValid);
        }
    }

    [Theory]
    [InlineData("https://storyteller.example.com/api")]
    [InlineData("https://storyteller.example.com:8443/api/")]
    [InlineData("http://localhost:7071/api")]
    [InlineData("http://LOCALHOST:7071/api")]
    [InlineData("http://127.0.0.1:7071/api")]
    [InlineData("http://127.0.0.2/api")]
    [InlineData("http://[::1]:7071/api")]
    [InlineData("http://loopback/api")]
    [InlineData("http://storyteller.example.com/api")]
    [InlineData("http://localhost.example.com/api")]
    [InlineData("http://10.0.0.1/api")]
    [InlineData("http://host.docker.internal:7071/api")]
    [InlineData("ftp://storyteller.example.com/api")]
    [InlineData("file:///c:/storyteller")]
    [InlineData("https://user:password@storyteller.example.com/api")]
    [InlineData("https://user@storyteller.example.com/api")]
    [InlineData("https://storyteller.example.com/api?code=1")]
    [InlineData("https://storyteller.example.com/api#fragment")]
    [InlineData("https://storyteller.example.com/api?")]
    [InlineData("storyteller.example.com/api")]
    [InlineData("/api")]
    [InlineData("")]
    public void TheEndpointRule_IsTheOneOfTheSdk(string endpoint)
    {
        var allowedBySdk = true;

        try
        {
            StorytellerUrlRule.RequireEndpoint(endpoint, "endpoint");
        }
        catch (StorytellerAuthenticationException)
        {
            allowedBySdk = false;
        }

        EndpointRule.IsAllowed(endpoint).ShouldBe(allowedBySdk);
    }
}
