using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class OptionsTests
{
    [Fact]
    public void Validate_CompleteEntraSettings_Succeed()
    {
        Validate(Valid()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AuthKit_FailsWithTheReason()
    {
        var options = Valid();
        options.Provider = McpIdentityProvider.AuthKit;

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldBe(McpHostOptionsValidator.AuthKitRefusal);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("")]
    public void Validate_TenantThatIsNotAGuid_Fails(string tenantId)
    {
        var options = Valid();
        options.EntraId.TenantId = tenantId;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:EntraId:TenantId");
    }

    [Fact]
    public void Validate_NoAllowedClient_Fails()
    {
        var options = Valid();
        options.EntraId.AllowedClientIds.Clear();

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:EntraId:AllowedClientIds");
    }

    [Fact]
    public void Validate_NoCredential_Fails()
    {
        var options = Valid();
        options.EntraId.ClientSecret = null;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("exactly one credential");
    }

    [Fact]
    public void Validate_TwoCredentials_Fail()
    {
        var options = Valid();
        options.EntraId.UseManagedIdentity = true;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("exactly one credential");
    }

    [Theory]
    [InlineData("http://mcp.test/mcp", "HTTPS")]
    [InlineData("https://mcp.test/mcp/", "slash")]
    [InlineData("mcp.test/mcp", "absolute")]
    public void Validate_BadResourceUrl_Fails(string url, string reason)
    {
        var options = Valid();
        options.ResourceUrl = url;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain(reason);
    }

    [Fact]
    public void Validate_HttpResourceUrlInDevelopment_Succeeds()
    {
        var options = Valid();
        options.ResourceUrl = "http://localhost:5180/mcp";

        Validate(options, Environments.Development).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void ValidateApi_HttpBaseUrlOutsideDevelopment_Fails()
    {
        var validator = new StorytellerApiOptionsValidator(new TestEnvironment(Environments.Production));

        var result = validator.Validate(null, new StorytellerApiOptions { BaseUrl = "http://api.test/api", Scopes = ["api://storyteller/User.Impersonation"] });

        result.FailureMessage.ShouldNotBeNull().ShouldContain("Storyteller:BaseUrl must use HTTPS");
    }

    [Fact]
    public void ValidateApi_NoScopes_Fails()
    {
        var validator = new StorytellerApiOptionsValidator(new TestEnvironment(Environments.Production));

        var result = validator.Validate(null, new StorytellerApiOptions { BaseUrl = "https://api.test/api" });

        result.FailureMessage.ShouldNotBeNull().ShouldContain("Storyteller:Scopes");
    }

    [Fact]
    public void Settings_NormalizeTheTenantAndDeriveTheUrls()
    {
        var options = Valid();
        options.EntraId.TenantId = TestTokens.TenantId.ToUpperInvariant();

        var settings = McpAuthSettings.From(options);

        settings.TenantId.ShouldBe(TestTokens.TenantId);
        settings.ValidIssuers.ShouldBe([$"https://login.microsoftonline.com/{TestTokens.TenantId}/v2.0", $"https://sts.windows.net/{TestTokens.TenantId}/"]);
        settings.Audience.ShouldBe(TestTokens.ResourceUrl);
        settings.FullScope.ShouldBe($"{TestTokens.ResourceUrl}/Mcp.Access");
        settings.Origin.ShouldBe("https://mcp.test");
        settings.ProtectedResourceMetadataUrl.ShouldBe("https://mcp.test/.well-known/oauth-protected-resource/mcp");
    }

    [Fact]
    public void Settings_ExplicitAudience_IsTheScopePrefix()
    {
        var options = Valid();
        options.EntraId.Audience = $"api://{TestTokens.McpClientId}";

        McpAuthSettings.From(options).FullScope.ShouldBe($"api://{TestTokens.McpClientId}/Mcp.Access");
    }

    [Fact]
    public void Host_WithAuthKit_FailsToStart()
    {
        using var host = new McpTestHost(new Dictionary<string, string?> { ["Mcp:Provider"] = "AuthKit" });

        var exception = Should.Throw<OptionsValidationException>(() => host.CreateClient());

        exception.Message.ShouldContain("Entra ID only");
    }

    private static McpHostOptions Valid()
    {
        return new McpHostOptions
        {
            ResourceUrl = TestTokens.ResourceUrl,
            EntraId = new McpEntraIdOptions
            {
                TenantId = TestTokens.TenantId,
                ClientId = TestTokens.McpClientId,
                AllowedClientIds = [TestTokens.ClientAppId],
                ClientSecret = "secret",
            },
        };
    }

    private static ValidateOptionsResult Validate(McpHostOptions options, string environment = "Production")
    {
        return new McpHostOptionsValidator(new TestEnvironment(environment)).Validate(null, options);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Mcp.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
