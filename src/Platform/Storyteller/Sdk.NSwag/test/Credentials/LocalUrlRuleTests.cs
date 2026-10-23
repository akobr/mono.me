using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class LocalUrlRuleTests
{
    [Theory]
    [InlineData("https://api.example.com/api")]
    [InlineData("https://api.example.com:8443/api/")]
    [InlineData("http://localhost:7071/api")]
    [InlineData("http://LOCALHOST/api")]
    [InlineData("http://127.0.0.1:7071/api")]
    [InlineData("http://[::1]:7071/api")]
    public void Allows(string url)
    {
        LocalUrlRule.IsAllowed(new Uri(url)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://loopback/api")]
    [InlineData("http://api.example.com/api")]
    [InlineData("http://10.0.0.5/api")]
    [InlineData("https://user:password@api.example.com/api")]
    [InlineData("https://user@api.example.com/api")]
    [InlineData("http://user@localhost/api")]
    [InlineData("ftp://api.example.com/api")]
    public void Refuses(string url)
    {
        LocalUrlRule.IsAllowed(new Uri(url)).ShouldBeFalse();
    }

    [Fact]
    public void Require_NamesTheSettingButNotTheValue()
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(() => LocalUrlRule.Require("https://user:password@api.example.com/api", "STORYTELLER_ENDPOINT"));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        exception.Message.ShouldNotContain("password");
    }

    [Fact]
    public void Describe_DropsUserInfoQueryAndFragment()
    {
        LocalUrlRule.Describe(new Uri("https://u:p4ss@idp.example.com:8443/oauth2/token?tenant=x#f")).ShouldBe("https://idp.example.com:8443/oauth2/token");
        LocalUrlRule.Describe(new Uri("https://api.example.com/api/")).ShouldBe("https://api.example.com/api/");
        LocalUrlRule.Describe(new Uri("/token", UriKind.Relative)).ShouldBe("<relative>");
        LocalUrlRule.Describe(null).ShouldBeNull();
    }

    [Fact]
    public void TheOptionTypes_PrintNoUserInfo_AndSurviveARelativeUrl()
    {
        // Options are printed before they are validated, so user info and a relative URL can still be in them.
        var withUserInfo = new Uri("https://u:p4ss@api.example.com/api");
        var relative = new Uri("/token", UriKind.Relative);

        string[] texts =
        [
            new ClientCredentialsOptions { TokenUrl = relative, Endpoint = withUserInfo, ClientId = "machine", ClientSecret = "s3cr3t" }.ToString(),
            new FederatedTokenOptions { Endpoint = withUserInfo, ClientId = "machine", SubjectTokenPath = "/run/token" }.ToString(),
            new StorytellerCredentialBinding(withUserInfo).ToString(),
        ];

        texts.ShouldAllBe(text => text.Contains("https://api.example.com/api") && !text.Contains("p4ss"));
        texts[0].ShouldContain("TokenUrl = <relative>");
    }

    [Fact]
    public void Require_RefusesARelativeUrl()
    {
        Should.Throw<StorytellerAuthenticationException>(() => LocalUrlRule.Require("/api", "STORYTELLER_ENDPOINT"));
    }

    [Theory]
    [InlineData("https://api.example.com/api?code=p4ssw0rd")]
    [InlineData("https://api.example.com/api#p4ssw0rd")]
    public void RequireEndpoint_RefusesAQueryOrAFragment_ThatPathsWouldFollow(string url)
    {
        LocalUrlRule.Require(url, "STORYTELLER_ENDPOINT").ShouldBe(new Uri(url));

        var exception = Should.Throw<StorytellerAuthenticationException>(() => LocalUrlRule.RequireEndpoint(url, "STORYTELLER_ENDPOINT"));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        exception.Message.ShouldContain("query or a fragment");
        exception.Message.ShouldNotContain("p4ssw0rd");
    }

    [Fact]
    public void RequireEndpoint_AppliesTheLocalUrlRule()
    {
        LocalUrlRule.RequireEndpoint("http://localhost:7071/api/", "STORYTELLER_ENDPOINT").ShouldBe(new Uri("http://localhost:7071/api/"));
        Should.Throw<StorytellerAuthenticationException>(() => LocalUrlRule.RequireEndpoint("http://loopback/api", "STORYTELLER_ENDPOINT"));
    }

    [Fact]
    public void AnEndpointWithAQuery_FailsTheCredentialOptions()
    {
        var endpoint = new Uri("https://api.example.com/api?x=1");

        Should.Throw<StorytellerAuthenticationException>(() => StorytellerCredential.FromFederatedTokenFile(
                new FederatedTokenOptions { Endpoint = endpoint, ClientId = "machine", SubjectTokenPath = "/run/token" }))
            .Message.ShouldContain("FederatedTokenOptions.Endpoint");
        Should.Throw<StorytellerAuthenticationException>(() => StorytellerCredential.FromClientCredentials(
                new ClientCredentialsOptions { Endpoint = endpoint, ClientId = "client_01", ClientSecret = "s3cr3t" }))
            .Message.ShouldContain("ClientCredentialsOptions.Endpoint");
    }
}
