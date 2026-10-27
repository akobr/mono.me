using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

// The package copies the SDK's variable names, the rules of FullKey and the SDK's URL rule, so an AppHost does not
// reference them; these tests keep the copies equal to the originals.
public sealed class ContractTests
{
    // Credential names of the SDK that the resource does not offer: processes started by an AppHost have no projected or
    // CI token, and the canonical names are read before the deprecated ones.
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
        StorytellerVariableNames.KEY.ShouldBe(StorytellerVariables.KEY);
    }

    // A new credential name in the SDK fails here until it is mapped or listed as not offered.
    [Fact]
    public void TheCredentialNames_AreTheOnesOfTheSdk_MinusThoseNotOffered()
    {
        StorytellerVariableNames.Credentials.ShouldBe(StorytellerVariables.Credentials.Except(NotOffered), ignoreOrder: true);
        NotOffered.ShouldBeSubsetOf(StorytellerVariables.Credentials);
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
