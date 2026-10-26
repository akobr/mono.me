using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class OptionsTests
{
    [Theory]
    [InlineData(McpIdentityProvider.EntraId)]
    [InlineData(McpIdentityProvider.AuthKit)]
    [InlineData(McpIdentityProvider.Keycloak)]
    public void Validate_CompleteSettings_Succeed(McpIdentityProvider provider)
    {
        Validate(Valid(provider)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_UnknownProvider_Fails()
    {
        var options = Valid();
        options.Provider = (McpIdentityProvider)42;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Provider '42' is not a known identity provider");
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("")]
    public void Validate_EntraTenantThatIsNotAGuid_Fails(string tenantId)
    {
        var options = Valid();
        options.Authentication.TenantId = tenantId;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:TenantId");
    }

    [Fact]
    public void Validate_EntraClientIdThatIsNotAGuid_Fails()
    {
        var options = Valid();
        options.Authentication.ClientId = "mcp";

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:ClientId must be the GUID");
    }

    [Fact]
    public void Validate_EntraWithoutAllowedClients_Fails()
    {
        var options = Valid();
        options.Authentication.AllowedClientIds.Clear();

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:AllowedClientIds");
    }

    [Fact]
    public void Validate_EntraAllowedClientThatIsNotAGuid_Fails()
    {
        var options = Valid();
        options.Authentication.AllowedClientIds.Add("vscode");

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:AllowedClientIds");
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d/v2.0")]
    [InlineData("https://login.microsoftonline.com/8A7B6C5D-4E3F-4A1B-8C9D-0E1F2A3B4C5D/v2.0/")]
    public void Validate_EntraIssuerOfTheTenant_Succeeds(string issuer)
    {
        var options = Valid();
        options.Authentication.Issuer = issuer;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/11111111-2222-4333-8444-555555555555/v2.0")]
    [InlineData("https://login.microsoftonline.com/common/v2.0")]
    [InlineData("https://auth.mcp.test")]
    public void Validate_EntraIssuerOfAnotherTenant_Fails(string issuer)
    {
        var options = Valid();
        options.Authentication.Issuer = issuer;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:Issuer must be empty or");
    }

    [Theory]
    [InlineData(McpIdentityProvider.AuthKit, "AuthKit domain")]
    [InlineData(McpIdentityProvider.Keycloak, "Keycloak realm")]
    public void Validate_IssuerProviderWithoutIssuer_Fails(McpIdentityProvider provider, string expected)
    {
        var options = Valid(provider);
        options.Authentication.Issuer = null;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain($"Mcp:Authentication:Issuer must be the {expected}");
    }

    [Theory]
    [InlineData(McpIdentityProvider.AuthKit, "http://auth.mcp.test", "HTTPS")]
    [InlineData(McpIdentityProvider.AuthKit, "https://auth.mcp.test/", "slash")]
    [InlineData(McpIdentityProvider.Keycloak, "keycloak.mcp.test/realms/storyteller", "absolute")]
    [InlineData(McpIdentityProvider.Keycloak, "ftp://keycloak.mcp.test/realms/storyteller", "absolute")]
    public void Validate_BadIssuer_Fails(McpIdentityProvider provider, string issuer, string reason)
    {
        var options = Valid(provider);
        options.Authentication.Issuer = issuer;

        var message = Validate(options).FailureMessage.ShouldNotBeNull();

        message.ShouldContain("Mcp:Authentication:Issuer");
        message.ShouldContain(reason);
    }

    // A local Keycloak runs over plain HTTP.
    [Fact]
    public void Validate_HttpIssuerInDevelopment_Succeeds()
    {
        var options = Valid(McpIdentityProvider.Keycloak);
        options.Authentication.Issuer = "http://localhost:8080/realms/storyteller";

        Validate(options, Environments.Development).Succeeded.ShouldBeTrue();
    }

    // Dynamic client registration: the clients are not known in advance.
    [Theory]
    [InlineData(McpIdentityProvider.AuthKit)]
    [InlineData(McpIdentityProvider.Keycloak)]
    public void Validate_IssuerProviderWithoutAllowedClients_Succeeds(McpIdentityProvider provider)
    {
        var options = Valid(provider);
        options.Authentication.AllowedClientIds.Clear();

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(McpIdentityProvider.AuthKit)]
    [InlineData(McpIdentityProvider.Keycloak)]
    public void Validate_IssuerProviderWithAnEmptyAllowedClient_Fails(McpIdentityProvider provider)
    {
        var options = Valid(provider);
        options.Authentication.AllowedClientIds.Add(" ");

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:AllowedClientIds must not contain empty client ids");
    }

    [Theory]
    [InlineData(McpIdentityProvider.AuthKit, "TenantId")]
    [InlineData(McpIdentityProvider.AuthKit, "ClientId")]
    [InlineData(McpIdentityProvider.Keycloak, "TenantId")]
    [InlineData(McpIdentityProvider.Keycloak, "ClientId")]
    public void Validate_EntraSettingUnderAnotherProvider_Fails(McpIdentityProvider provider, string setting)
    {
        var options = Valid(provider);

        if (setting == "TenantId")
        {
            options.Authentication.TenantId = TestTokens.TenantId;
        }
        else
        {
            options.Authentication.ClientId = TestTokens.McpClientId;
        }

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain($"Mcp:Authentication:{setting} applies to Entra ID only");
    }

    [Theory]
    [InlineData(McpIdentityProvider.EntraId, "Mcp.Access Other")]
    [InlineData(McpIdentityProvider.AuthKit, "")]
    [InlineData(McpIdentityProvider.Keycloak, "openid Mcp.Access")]
    public void Validate_ScopeThatIsNotOneName_Fails(McpIdentityProvider provider, string scope)
    {
        var options = Valid(provider);
        options.Authentication.Scope = scope;

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:Scope must be one scope name");
    }

    [Theory]
    [InlineData(McpIdentityProvider.EntraId)]
    [InlineData(McpIdentityProvider.Keycloak)]
    public void Validate_AudienceWithATrailingSlash_Fails(McpIdentityProvider provider)
    {
        var options = Valid(provider);
        options.Authentication.Audience = "https://mcp.test/mcp/";

        Validate(options).FailureMessage.ShouldNotBeNull().ShouldContain("Mcp:Authentication:Audience must not end with a slash");
    }

    [Fact]
    public void Validate_EveryProblemIsReported()
    {
        var options = Valid(McpIdentityProvider.Keycloak);
        options.Authentication.Issuer = "http://keycloak.mcp.test/realms/storyteller";
        options.Authentication.TenantId = TestTokens.TenantId;
        options.Authentication.Scope = string.Empty;

        Validate(options).Failures.ShouldNotBeNull().Count().ShouldBe(3);
    }

    public static TheoryData<string, McpActorOptions> ValidActors() => new()
    {
        { "managed identity", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "api://storyteller-token-exchange" } } },
        { "user-assigned identity", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "api://storyteller-token-exchange", ClientId = "4f1c2d3e-5a6b-4c7d-8e9f-0a1b2c3d4e5f" } } },
        { "token file", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = "/var/run/secrets/tokens/storyteller" } } },
        { "client assertion key", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePath = "/etc/mcp/actor.pfx", CertificatePassword = "secret" } } },
        { "key without password", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePath = "/etc/mcp/actor.pfx" } } },
        { "assertion audience of the public token endpoint", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePath = "/etc/mcp/actor.pfx", Audience = "https://storyteller.example.com/api/v1/access/token" } } },
        { "assertion audience of the issuer", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePath = "/etc/mcp/actor.pfx", Audience = "https://storyteller.example.com" } } },
    };

    public static TheoryData<string, McpActorOptions, string> InvalidActors() => new()
    {
        { "no id", new McpActorOptions { WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = "/token" } }, "Mcp:Actor:Id must name the actor" },
        { "no credential", new McpActorOptions { Id = "mcp" }, "exactly one credential of the actor" },
        { "empty templates", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Audience = " " }, ClientAssertionKey = new(), ClientSecret = string.Empty }, "exactly one credential of the actor" },
        { "two credentials", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = "/token" }, ClientAssertionKey = new() { CertificatePath = "/actor.pfx" } }, "exactly one credential of the actor" },
        { "no kind", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { TokenFilePath = "/token" } }, "Mcp:Actor:WorkloadIdentity:Kind must be ManagedIdentity or TokenFile" },
        { "unknown kind", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = (McpWorkloadIdentityKind)42, TokenFilePath = "/token" } }, "Mcp:Actor:WorkloadIdentity:Kind must be ManagedIdentity or TokenFile" },
        { "managed identity without audience", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity } }, "Mcp:Actor:WorkloadIdentity:Audience must be the audience" },
        { "audience with /.default", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "api://storyteller-token-exchange/.default" } }, "without /.default" },
        { "Entra federation audience", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "api://AzureADTokenExchange" } }, "belongs to another service" },
        { "Entra federation audience with a slash", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "api://AzureADTokenExchange/" } }, "belongs to another service" },
        { "Entra federation audience in another case", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = " API://azureadtokenexchange " } }, "belongs to another service" },
        { "Azure resource audience", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "https://management.azure.com" } }, "belongs to another service" },
        { "managed identity with a token file", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.ManagedIdentity, Audience = "api://storyteller-token-exchange", TokenFilePath = "/token" } }, "TokenFilePath applies to the TokenFile kind only" },
        { "token file without path", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.TokenFile } }, "Mcp:Actor:WorkloadIdentity:TokenFilePath must name the token file" },
        { "token file with an audience", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = "/token", Audience = "api://storyteller-token-exchange" } }, "apply to the ManagedIdentity kind only" },
        { "token file with a client id", new McpActorOptions { Id = "mcp", WorkloadIdentity = new() { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = "/token", ClientId = "4f1c2d3e-5a6b-4c7d-8e9f-0a1b2c3d4e5f" } }, "apply to the ManagedIdentity kind only" },
        { "key without path", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePassword = "secret" } }, "Mcp:Actor:ClientAssertionKey:CertificatePath must name the PKCS#12 file" },
        { "assertion audience without a key", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { Audience = "https://storyteller.example.com/api/v1/access/token" } }, "Mcp:Actor:ClientAssertionKey:CertificatePath must name the PKCS#12 file" },
        { "relative assertion audience", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePath = "/etc/mcp/actor.pfx", Audience = "/api/v1/access/token" } }, "Mcp:Actor:ClientAssertionKey:Audience must be empty or the API's token endpoint" },
        { "assertion audience with spaces", new McpActorOptions { Id = "mcp", ClientAssertionKey = new() { CertificatePath = "/etc/mcp/actor.pfx", Audience = " https://storyteller.example.com/api/v1/access/token" } }, "Mcp:Actor:ClientAssertionKey:Audience must be empty or the API's token endpoint" },
        { "secret outside Development", new McpActorOptions { Id = "mcp", ClientSecret = "secret" }, "Mcp:Actor:ClientSecret is accepted in the Development environment only" },
    };

    [Theory]
    [MemberData(nameof(ValidActors))]
    public void Validate_ActorWithOneCredential_Succeeds(string because, McpActorOptions actor)
    {
        var options = Valid();
        options.Actor = actor;

        Validate(options).Succeeded.ShouldBeTrue(because);
    }

    [Theory]
    [MemberData(nameof(InvalidActors))]
    public void Validate_BadActor_Fails(string because, McpActorOptions actor, string expected)
    {
        var options = Valid();
        options.Actor = actor;

        Validate(options).FailureMessage.ShouldNotBeNull(because).ShouldContain(expected, Case.Sensitive, because);
    }

    [Fact]
    public void Validate_ClientSecretInDevelopment_Succeeds()
    {
        var options = Valid();
        options.Actor = new McpActorOptions { Id = "mcp", ClientSecret = "local-secret" };

        Validate(options, Environments.Development).Succeeded.ShouldBeTrue();
    }

    // The template of appsettings.json names every key with an empty value: no credential is set by it.
    [Fact]
    public void Bind_EmptyActorTemplate_SetsNoCredential()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Actor:Id"] = "mcp",
                ["Mcp:Actor:WorkloadIdentity:Kind"] = string.Empty,
                ["Mcp:Actor:WorkloadIdentity:Audience"] = string.Empty,
                ["Mcp:Actor:WorkloadIdentity:ClientId"] = string.Empty,
                ["Mcp:Actor:WorkloadIdentity:TokenFilePath"] = string.Empty,
                ["Mcp:Actor:ClientAssertionKey:CertificatePath"] = string.Empty,
                ["Mcp:Actor:ClientAssertionKey:CertificatePassword"] = string.Empty,
                ["Mcp:Actor:ClientAssertionKey:Audience"] = string.Empty,
                ["Mcp:Actor:ClientSecret"] = string.Empty,
            })
            .Build();

        var actor = configuration.GetSection(McpHostOptions.SectionName).Get<McpHostOptions>().ShouldNotBeNull().Actor;

        actor.WorkloadIdentity.ShouldNotBeNull().Kind.ShouldBeNull();
        actor.HasWorkloadIdentity.ShouldBeFalse();
        actor.HasClientAssertionKey.ShouldBeFalse();
        actor.HasClientSecret.ShouldBeFalse();
    }

    [Fact]
    public void Bind_WorkloadIdentityKind_IsReadByName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Actor:WorkloadIdentity:Kind"] = "TokenFile" })
            .Build();

        configuration.GetSection(McpHostOptions.SectionName).Get<McpHostOptions>()!.Actor.WorkloadIdentity!.Kind.ShouldBe(McpWorkloadIdentityKind.TokenFile);
    }

    [Fact]
    public void Host_WithTheActorSecretOutsideDevelopment_FailsToStart()
    {
        using var host = new McpTestHost(new Dictionary<string, string?>
        {
            ["Mcp:Actor:WorkloadIdentity:Kind"] = null,
            ["Mcp:Actor:WorkloadIdentity:TokenFilePath"] = null,
            ["Mcp:Actor:ClientSecret"] = "local-secret",
        });

        var exception = Should.Throw<OptionsValidationException>(() => host.CreateClient());

        exception.Message.ShouldContain("Mcp:Actor:ClientSecret is accepted in the Development environment only");
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

        var result = validator.Validate(null, new StorytellerApiOptions { BaseUrl = "http://api.test/api" });

        result.FailureMessage.ShouldNotBeNull().ShouldContain("Storyteller:BaseUrl must use HTTPS");
    }

    // The exchange gives the token: the API needs no scopes from the settings.
    [Fact]
    public void ValidateApi_BaseUrlAlone_Succeeds()
    {
        var validator = new StorytellerApiOptionsValidator(new TestEnvironment(Environments.Production));

        validator.Validate(null, new StorytellerApiOptions { BaseUrl = "https://api.test/api" }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Settings_EntraNormalizeTheTenantAndDeriveTheUrls()
    {
        var options = Valid();
        options.Authentication.TenantId = TestTokens.TenantId.ToUpperInvariant();

        var settings = McpAuthSettings.From(options);

        settings.TenantId.ShouldBe(TestTokens.TenantId);
        settings.Issuer.ShouldBe(TestTokens.V2Issuer);
        settings.ValidIssuers.ShouldBe([TestTokens.V2Issuer, $"https://sts.windows.net/{TestTokens.TenantId}/"]);
        settings.ValidAudiences.ShouldBe([TestTokens.McpClientId, TestTokens.ResourceUrl]);
        settings.MetadataAddress.ShouldBe($"{TestTokens.V2Issuer}/.well-known/openid-configuration");
        settings.Audience.ShouldBe(TestTokens.ResourceUrl);
        settings.FullScope.ShouldBe($"{TestTokens.ResourceUrl}/Mcp.Access");
        settings.Origin.ShouldBe("https://mcp.test");
        settings.AuthorizationServer.ShouldBe("https://mcp.test");
        settings.ServesAuthorizationServerMetadata.ShouldBeTrue();
        settings.ProtectedResourceMetadataUrl.ShouldBe("https://mcp.test/.well-known/oauth-protected-resource/mcp");
    }

    [Fact]
    public void Settings_EntraExplicitAudience_IsTheScopePrefix()
    {
        var options = Valid();
        options.Authentication.Audience = $"api://{TestTokens.McpClientId}";

        McpAuthSettings.From(options).FullScope.ShouldBe($"api://{TestTokens.McpClientId}/Mcp.Access");
    }

    [Theory]
    [InlineData(McpIdentityProvider.AuthKit, TestTokens.AuthKitIssuer)]
    [InlineData(McpIdentityProvider.Keycloak, TestTokens.KeycloakIssuer)]
    public void Settings_IssuerProvider_PointAtTheIssuer(McpIdentityProvider provider, string issuer)
    {
        var settings = McpAuthSettings.From(Valid(provider));

        settings.Issuer.ShouldBe(issuer);
        settings.ValidIssuers.ShouldBe([issuer]);
        settings.ValidAudiences.ShouldBe([TestTokens.ResourceUrl]);
        settings.MetadataAddress.ShouldBe($"{issuer}/.well-known/openid-configuration");
        settings.FullScope.ShouldBe(TestTokens.Scope);
        settings.AuthorizationServer.ShouldBe(issuer);
        settings.ServesAuthorizationServerMetadata.ShouldBeFalse();
        settings.TenantId.ShouldBeNull();
        settings.ClientId.ShouldBeNull();
        settings.Authority.ShouldBeNull();
        settings.ProtectedResourceMetadataUrl.ShouldBe("https://mcp.test/.well-known/oauth-protected-resource/mcp");
    }

    [Fact]
    public void Settings_IssuerProviderExplicitAudience_IsTheOnlyAudience()
    {
        var options = Valid(McpIdentityProvider.Keycloak);
        options.Authentication.Audience = "storyteller-mcp";

        var settings = McpAuthSettings.From(options);

        settings.ValidAudiences.ShouldBe(["storyteller-mcp"]);
        settings.FullScope.ShouldBe(TestTokens.Scope);
    }

    // Client ids of AuthKit and Keycloak are compared as they are; Entra's are GUIDs in any case.
    [Fact]
    public void Settings_AllowedClients_FollowTheProvider()
    {
        var entra = Valid();
        entra.Authentication.AllowedClientIds = [TestTokens.ClientAppId.ToUpperInvariant()];
        var keycloak = Valid(McpIdentityProvider.Keycloak);
        keycloak.Authentication.AllowedClientIds = [$" {TestTokens.KeycloakClientId} "];

        McpAuthSettings.From(entra).AllowedClientIds.Contains(TestTokens.ClientAppId).ShouldBeTrue();
        McpAuthSettings.From(keycloak).AllowedClientIds.ShouldBe([TestTokens.KeycloakClientId]);
        McpAuthSettings.From(keycloak).AllowedClientIds.Contains(TestTokens.KeycloakClientId.ToUpperInvariant()).ShouldBeFalse();
    }

    // A local Keycloak serves its metadata over plain HTTP; only Development may fetch it so.
    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public void Configure_RequiresHttpsMetadataOutsideDevelopment(string environment, bool required)
    {
        var options = new JwtBearerOptions();

        McpAuthentication.Configure(options, McpAuthSettings.From(Valid(McpIdentityProvider.Keycloak)), new TestEnvironment(environment));

        options.RequireHttpsMetadata.ShouldBe(required);
    }

    [Theory]
    [InlineData(McpIdentityProvider.EntraId)]
    [InlineData(McpIdentityProvider.AuthKit)]
    [InlineData(McpIdentityProvider.Keycloak)]
    public void Configure_PinsTheIssuersAudiencesAndRs256(McpIdentityProvider provider)
    {
        var options = new JwtBearerOptions();
        var settings = McpAuthSettings.From(Valid(provider));

        McpAuthentication.Configure(options, settings, new TestEnvironment(Environments.Production));

        options.MetadataAddress.ShouldBe(settings.MetadataAddress);
        options.TokenValidationParameters.ValidIssuers.ShouldBe(settings.ValidIssuers);
        options.TokenValidationParameters.ValidAudiences.ShouldBe(settings.ValidAudiences);
        options.TokenValidationParameters.ValidAlgorithms.ShouldBe([SecurityAlgorithms.RsaSha256]);

        // The API's skew, not the default 5 minutes: the exchange refuses what the default would still let in.
        options.TokenValidationParameters.ClockSkew.ShouldBe(McpTokenRules.ClockSkew);
    }

    [Theory]
    [InlineData("AuthKit")]
    [InlineData("Keycloak")]
    public void Host_WithAnIssuerProvider_Starts(string provider)
    {
        using var host = new McpTestHost(new Dictionary<string, string?> { ["Mcp:Provider"] = provider });

        using var client = host.CreateClient();

        client.ShouldNotBeNull();
    }

    [Fact]
    public void Host_WithKeycloakWithoutAnIssuer_FailsToStart()
    {
        using var host = new McpTestHost(new Dictionary<string, string?> { ["Mcp:Provider"] = "Keycloak", ["Mcp:Authentication:Issuer"] = null });

        var exception = Should.Throw<OptionsValidationException>(() => host.CreateClient());

        exception.Message.ShouldContain("Mcp:Authentication:Issuer must be the Keycloak realm");
    }

    private static McpHostOptions Valid(McpIdentityProvider provider = McpIdentityProvider.EntraId)
    {
        var authentication = provider switch
        {
            McpIdentityProvider.AuthKit => new McpAuthenticationOptions
            {
                Issuer = TestTokens.AuthKitIssuer,
                AllowedClientIds = [TestTokens.AuthKitClientId],
            },
            McpIdentityProvider.Keycloak => new McpAuthenticationOptions
            {
                Issuer = TestTokens.KeycloakIssuer,
                AllowedClientIds = [TestTokens.KeycloakClientId],
            },
            _ => new McpAuthenticationOptions
            {
                TenantId = TestTokens.TenantId,
                ClientId = TestTokens.McpClientId,
                AllowedClientIds = [TestTokens.ClientAppId],
            },
        };

        return new McpHostOptions
        {
            Provider = provider,
            ResourceUrl = TestTokens.ResourceUrl,
            Authentication = authentication,
            Actor = new McpActorOptions
            {
                Id = "mcp",
                WorkloadIdentity = new McpWorkloadIdentityOptions { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = "/var/run/secrets/tokens/storyteller" },
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
