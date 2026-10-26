using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class TokenExchangeOptionsTests
{
    private const string Section = "Auth:TokenExchange";
    private const string ApiClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11";
    private const string TenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    private const string AuthKitIssuer = "https://api.workos.com/";
    private const string AuthKitDomain = "https://auth.example.com";
    private const string SecretHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private const string PublicBaseUrl = "https://storyteller.example.com/api";

    [Fact]
    public void AddUserAuthenticationOptions_WithoutTheSection_DisablesTheExchange()
    {
        var options = Resolve(EntraSettings());

        options.Enabled.ShouldBeFalse();
        options.AdditionalIssuers.ShouldBeEmpty();
        options.Actors.ShouldBeEmpty();
        options.TokenLifetimeMinutes.ShouldBe(10);
        options.MaxExchangesPerSubjectTokenPerMinute.ShouldBe(10);
    }

    [Fact]
    public void AddUserAuthenticationOptions_BindsTheActorsAndTheirDefaults()
    {
        var options = Resolve(EntraSettings(new Dictionary<string, string?>
        {
            [$"{Section}:Enabled"] = "true",
            [$"{Section}:TokenLifetimeMinutes"] = "15",
            [$"{Section}:MaxExchangesPerSubjectTokenPerMinute"] = "3",
            [$"{Section}:Actors:0:Id"] = "mcp",
            [$"{Section}:Actors:0:Authentication:WorkloadIdentity:Issuer"] = $"https://login.microsoftonline.com/{TenantId}/v2.0",
            [$"{Section}:Actors:0:Authentication:WorkloadIdentity:Subject"] = "managed-identity-oid",
            [$"{Section}:Actors:0:Authentication:WorkloadIdentity:Audience"] = "api://storyteller-token-exchange",
            [$"{Section}:Actors:0:SubjectAudiences:0"] = "https://mcp.example.com/mcp",
            [$"{Section}:Actors:0:AllowedClientIds:0"] = "vscode",
            [$"{Section}:Actors:1:Id"] = "reviewer",
            [$"{Section}:Actors:1:Authentication:ClientAssertion:JwksUri"] = "https://reviewer.example.com/.well-known/jwks.json",
            [$"{Section}:Actors:1:SubjectAudiences:0"] = "https://reviewer.example.com",
            [$"{Section}:Actors:1:Scopes:0"] = "User.Impersonation",
            [$"{Section}:Actors:1:AllowSecrets"] = "true",
            ["MachineAuth:Federation:PublicBaseUrl"] = PublicBaseUrl,
        }));

        options.Enabled.ShouldBeTrue();
        options.TokenLifetimeMinutes.ShouldBe(15);
        options.MaxExchangesPerSubjectTokenPerMinute.ShouldBe(3);
        options.Actors.Count.ShouldBe(2);

        var mcp = options.Actors[0];
        mcp.Id.ShouldBe("mcp");
        mcp.Authentication.WorkloadIdentity.ShouldNotBeNull().Subject.ShouldBe("managed-identity-oid");
        mcp.Authentication.ClientAssertion.ShouldBeNull();
        mcp.Authentication.ClientSecretHash.ShouldBeNull();
        mcp.SubjectAudiences.ShouldBe(["https://mcp.example.com/mcp"]);
        mcp.AllowedClientIds.ShouldBe(["vscode"]);
        mcp.GetScopes().ShouldBe([UserScopes.Impersonation]);
        mcp.GetRestrictions().ShouldBe([ActorRestrictions.NoSecretReveal]);

        var reviewer = options.Actors[1];
        reviewer.Authentication.ClientAssertion.ShouldNotBeNull().JwksUri.ShouldBe("https://reviewer.example.com/.well-known/jwks.json");
        reviewer.Authentication.WorkloadIdentity.ShouldBeNull();
        reviewer.GetScopes().ShouldBe([UserScopes.Impersonation]);
        reviewer.GetRestrictions().ShouldBeEmpty();
    }

    // Binding appends to arrays, so a second registration must not bind the section again.
    [Fact]
    public void AddUserAuthenticationOptions_Twice_BindsTheArraysOnce()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(EntraSettings(new Dictionary<string, string?>
        {
            [$"{Section}:Actors:0:Id"] = "mcp",
            [$"{Section}:Actors:0:Authentication:ClientAssertion:JwksUri"] = "https://mcp.example.com/.well-known/jwks.json",
            [$"{Section}:Actors:0:SubjectAudiences:0"] = "https://mcp.example.com/mcp",
            [$"{Section}:Actors:0:Scopes:0"] = "User.Impersonation",
            [$"{Section}:Actors:0:Restrictions:0"] = "NoSecretReveal",
            ["MachineAuth:Federation:PublicBaseUrl"] = PublicBaseUrl,
        })).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddFederationOptions(configuration);
        services.AddUserAuthenticationOptions(configuration);
        services.AddUserAuthenticationOptions(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TokenExchangeOptions>>().Value;

        var actor = options.Actors.ShouldHaveSingleItem();
        actor.SubjectAudiences.ShouldBe(["https://mcp.example.com/mcp"]);
        actor.Scopes.ShouldBe([UserScopes.Impersonation]);
        actor.Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
        services.Count(descriptor => descriptor.ServiceType == typeof(IValidateOptions<TokenExchangeOptions>)).ShouldBe(1);
    }

    [Fact]
    public async Task AddUserAuthenticationOptions_InvalidExchange_FailsWhenTheHostStarts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(EntraSettings(new Dictionary<string, string?>
        {
            [$"{Section}:Enabled"] = "true",
        }));
        builder.Services.AddUserAuthenticationOptions(builder.Configuration);
        using var host = builder.Build();

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain($"{Section}:Actors must list at least one actor");
    }

    [Fact]
    public void Validate_DefaultsAndAValidActor_Succeed()
    {
        Validate(new TokenExchangeOptions()).Succeeded.ShouldBeTrue();
        Validate(new TokenExchangeOptions { Enabled = true, Actors = [Actor()] }).Succeeded.ShouldBeTrue();
    }

    // Nothing to check against Auth:Provider: the user options are not read, so their validation does not run twice.
    [Fact]
    public void Validate_WithoutActorsOrIssuers_DoesNotReadTheUserOptions()
    {
        var validator = new TokenExchangeOptionsValidator(
            new TestHostEnvironment(),
            new ThrowingOptions<UserAuthenticationOptions>(),
            new ThrowingOptions<FederationOptions>());

        validator.Validate(null, new TokenExchangeOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EnabledWithoutActors_Fails()
    {
        var result = Validate(new TokenExchangeOptions { Enabled = true });

        result.Failures.ShouldBe([$"{Section}:Actors must list at least one actor when {Section}:Enabled is true."]);
    }

    [Fact]
    public void Validate_CollectsEveryFailure()
    {
        var result = Validate(new TokenExchangeOptions
        {
            Enabled = true,
            TokenLifetimeMinutes = 60,
            Actors = [Actor(actor => actor.Id = string.Empty), Actor(actor => actor.SubjectAudiences = [])],
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull();
        result.Failures.Count().ShouldBe(3);
        result.Failures.ShouldContain($"{Section}:Actors:0:Id is required.");
        result.Failures.ShouldContain(failure => failure.StartsWith($"{Section}:Actors:1:SubjectAudiences must list", StringComparison.Ordinal));
        result.Failures.ShouldContain(failure => failure.StartsWith($"{Section}:TokenLifetimeMinutes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void Validate_TokenLifetime_IsFiveToFifteenMinutes(int minutes, bool valid)
    {
        Validate(new TokenExchangeOptions { TokenLifetimeMinutes = minutes }).Succeeded.ShouldBe(valid);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Validate_ExchangesPerSubjectToken_AreOneToSixtyAMinute(int exchanges, bool valid)
    {
        Validate(new TokenExchangeOptions { MaxExchangesPerSubjectTokenPerMinute = exchanges }).Succeeded.ShouldBe(valid);
    }

    [Theory]
    [InlineData("mcp", true)]
    [InlineData("mcp-server_1.0~a", true)]
    [InlineData(" ", false)]
    [InlineData("mcp server", false)]
    [InlineData("mcp/1", false)]
    [InlineData("mcp:1", false)]
    [InlineData("mcpé", false)]
    public void Validate_ActorId_IsUrlSafe(string id, bool valid)
    {
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Id = id)] }).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Validate_ActorId_IsAtMostSixtyFourCharacters()
    {
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Id = new string('a', 64))] }).Succeeded.ShouldBeTrue();
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Id = new string('a', 65))] }).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ActorIds_AreUniqueIgnoringCase()
    {
        var result = Validate(new TokenExchangeOptions { Actors = [Actor(), Actor(actor => actor.Id = "MCP")] });

        result.Failures.ShouldBe([$"{Section}:Actors:1:Id 'MCP' is not unique; actor ids are compared ignoring case."]);
    }

    [Fact]
    public void Validate_ActorWithoutAnAuthenticationMethod_Fails()
    {
        var result = Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Authentication = new ActorAuthenticationOptions())] });

        result.Failures.ShouldBe([$"{Section}:Actors:0:Authentication must set exactly one of WorkloadIdentity, ClientAssertion and ClientSecretHash."]);
    }

    [Fact]
    public void Validate_ActorWithTwoAuthenticationMethods_Fails()
    {
        var result = Validate(
            new TokenExchangeOptions
            {
                Actors = [Actor(actor => actor.Authentication.ClientAssertion = new ActorClientAssertionOptions { JwksUri = "https://mcp.example.com/jwks" })],
            });

        result.Failures.ShouldBe([$"{Section}:Actors:0:Authentication must set exactly one of WorkloadIdentity, ClientAssertion and ClientSecretHash."]);
    }

    [Fact]
    public void Validate_WorkloadIdentity_NeedsAnHttpsIssuerASubjectAndAnAudience()
    {
        var result = Validate(new TokenExchangeOptions
        {
            Actors =
            [
                Actor(actor => actor.Authentication.WorkloadIdentity = new ActorWorkloadIdentityOptions
                {
                    Issuer = "http://login.microsoftonline.com/tenant/v2.0",
                    Subject = " ",
                    Audience = string.Empty,
                    JwksUri = "http://login.microsoftonline.com/tenant/discovery/v2.0/keys",
                }),
            ],
        });

        result.Failures.ShouldBe(
        [
            $"{Section}:Actors:0:Authentication:WorkloadIdentity:Issuer must be an absolute HTTPS URL.",
            $"{Section}:Actors:0:Authentication:WorkloadIdentity:Subject is required.",
            $"{Section}:Actors:0:Authentication:WorkloadIdentity:Audience is required.",
            $"{Section}:Actors:0:Authentication:WorkloadIdentity:JwksUri must be an absolute HTTPS URL when set.",
        ]);
    }

    [Fact]
    public void Validate_WorkloadIdentity_AcceptsAnHttpsJwksUri()
    {
        var result = Validate(new TokenExchangeOptions
        {
            Actors = [Actor(actor => actor.Authentication.WorkloadIdentity!.JwksUri = "https://token.actions.githubusercontent.com/.well-known/jwks")],
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://mcp.example.com/.well-known/jwks.json", true)]
    [InlineData("http://mcp.example.com/.well-known/jwks.json", false)]
    [InlineData("/.well-known/jwks.json", false)]
    [InlineData("", false)]
    public void Validate_ClientAssertion_NeedsAnHttpsJwksUri(string jwksUri, bool valid)
    {
        var result = Validate(new TokenExchangeOptions
        {
            Actors =
            [
                Actor(actor => actor.Authentication = new ActorAuthenticationOptions
                {
                    ClientAssertion = new ActorClientAssertionOptions { JwksUri = jwksUri },
                }),
            ],
        });

        result.Succeeded.ShouldBe(valid);
    }

    // The metadata publishes the token endpoint as the aud of client assertions; only PublicBaseUrl names it. Checked
    // also while the exchange is off.
    [Theory]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(true, " ")]
    public void Validate_ClientAssertion_NeedsPublicBaseUrl(bool enabled, string? publicBaseUrl)
    {
        var result = Validate(
            new TokenExchangeOptions { Enabled = enabled, Actors = [AssertionActor()] },
            federation: new FederationOptions { PublicBaseUrl = publicBaseUrl });

        result.Failures.ShouldBe(
        [
            $"{Section}:Actors:0:Authentication:ClientAssertion needs MachineAuth:Federation:PublicBaseUrl: a client assertion's aud names the token endpoint the metadata publishes, or MachineAuth:Federation:Issuer.",
        ]);
    }

    [Fact]
    public void Validate_ClientAssertion_WithPublicBaseUrl_Succeeds()
    {
        var result = Validate(
            new TokenExchangeOptions { Enabled = true, Actors = [AssertionActor()] },
            federation: new FederationOptions { PublicBaseUrl = PublicBaseUrl });

        result.Succeeded.ShouldBeTrue();
    }

    // Workload identity and client secret actors never resolve the federation options.
    [Fact]
    public void Validate_ActorsWithoutAClientAssertion_DoNotReadTheFederationOptions()
    {
        var validator = new TokenExchangeOptionsValidator(
            new TestHostEnvironment(Environments.Development),
            Options.Create(EntraUser()),
            new ThrowingOptions<FederationOptions>());

        validator.Validate(null, new TokenExchangeOptions { Enabled = true, Actors = [Actor(), SecretActor(SecretHash, "dev")] })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ClientSecretHash_IsAcceptedInDevelopment()
    {
        var options = new TokenExchangeOptions { Actors = [SecretActor(SecretHash)] };

        Validate(options, Environments.Development).Succeeded.ShouldBeTrue();
        Validate(new TokenExchangeOptions { Actors = [SecretActor(SecretHash.ToUpperInvariant())] }, Environments.Development).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Validate_ClientSecretHash_IsRefusedOutsideDevelopment(string environment)
    {
        var result = Validate(new TokenExchangeOptions { Actors = [SecretActor(SecretHash)] }, environment);

        result.Failures.ShouldBe([$"{Section}:Actors:0:Authentication:ClientSecretHash is accepted in the Development environment only; use WorkloadIdentity or ClientAssertion."]);
    }

    [Theory]
    [InlineData("s3cr3t")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08a")]
    [InlineData("zf86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Validate_ClientSecretHash_IsAHexSha256(string hash)
    {
        var result = Validate(new TokenExchangeOptions { Actors = [SecretActor(hash)] }, Environments.Development);

        result.Failures.ShouldBe([$"{Section}:Actors:0:Authentication:ClientSecretHash must be the hex SHA-256 hash of the secret (64 hex digits)."]);
    }

    [Fact]
    public void Validate_SubjectAudiences_AreRequiredAndNotBlank()
    {
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = [])] })
            .Failures.ShouldBe([$"{Section}:Actors:0:SubjectAudiences must list at least one audience (the actor's resource URL)."]);
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = ["https://mcp.example.com/mcp", " "])] })
            .Failures.ShouldBe([$"{Section}:Actors:0:SubjectAudiences entries must not be blank."]);
    }

    // A token issued for the API is not exchangeable, and the API keeps accepting its own users' tokens.
    [Theory]
    [InlineData($"api://{ApiClientId}")]
    [InlineData(ApiClientId)]
    [InlineData("API://6C0F1A2E-1F43-4B0E-9A4D-2F5E8F0C9B11")]
    [InlineData($"api://{ApiClientId}/")]
    [InlineData($"{ApiClientId}/")]
    public void Validate_SubjectAudiences_DoNotNameTheEntraApi(string audience)
    {
        var result = Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = [audience])] });

        result.Failures.ShouldBe([$"{Section}:Actors:0:SubjectAudiences must not name the API's own audience '{audience}'."]);
    }

    [Fact]
    public void Validate_SubjectAudiences_DoNotNameTheAuthKitOrKeycloakApiAudience()
    {
        var authKit = AuthKitUser();
        authKit.AuthKit.Audience = "https://storyteller.example.com";
        var keycloak = new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions { Issuer = "https://keycloak.example.com/realms/storyteller", Audience = "storyteller-api" },
        };

        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = ["https://storyteller.example.com"])] }, user: authKit)
            .Succeeded.ShouldBeFalse();
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = ["storyteller-api"])] }, user: keycloak)
            .Succeeded.ShouldBeFalse();
        Validate(new TokenExchangeOptions { Actors = [Actor()] }, user: keycloak).Succeeded.ShouldBeTrue();

        // The token handlers ignore a trailing '/' when they match aud, so the check does too, on both sides.
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = ["https://storyteller.example.com/"])] }, user: authKit)
            .Failures.ShouldBe([$"{Section}:Actors:0:SubjectAudiences must not name the API's own audience 'https://storyteller.example.com/'."]);
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = ["storyteller-api/"])] }, user: keycloak)
            .Succeeded.ShouldBeFalse();
        keycloak.Keycloak.Audience = "https://keycloak-api.example.com/";
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.SubjectAudiences = ["https://keycloak-api.example.com"])] }, user: keycloak)
            .Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Validate_AllowedClientIds_AreOptionalButNotBlank()
    {
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.AllowedClientIds = [])] }).Succeeded.ShouldBeTrue();
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.AllowedClientIds = ["vscode", string.Empty])] })
            .Failures.ShouldBe([$"{Section}:Actors:0:AllowedClientIds entries must not be blank."]);
    }

    [Fact]
    public void Validate_Scopes_ContainUserImpersonation()
    {
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Scopes = ["User.Impersonation", "Annotation.Read"])] })
            .Succeeded.ShouldBeTrue();
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Scopes = ["Annotation.Read"])] })
            .Failures.ShouldBe([$"{Section}:Actors:0:Scopes must contain User.Impersonation."]);
    }

    [Fact]
    public void Validate_Scopes_AreStorytellerScopes()
    {
        var result = Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Scopes = ["User.Impersonation", "user.impersonation", " "])] });

        result.Failures.ShouldBe(
        [
            $"{Section}:Actors:0:Scopes entries must not be blank.",
            $"{Section}:Actors:0:Scopes names 'user.impersonation', which is not a Storyteller scope.",
        ]);
    }

    [Theory]
    [InlineData("NoSecretReveal", true)]
    [InlineData("noSecretReveal", false)]
    [InlineData("NoWrites", false)]
    public void Validate_Restrictions_AreKnown(string restriction, bool valid)
    {
        Validate(new TokenExchangeOptions { Actors = [Actor(actor => actor.Restrictions = [restriction])] }).Succeeded.ShouldBe(valid);
    }

    [Fact]
    public void Validate_AllowSecretsWithAnExplicitNoSecretReveal_Fails()
    {
        var result = Validate(new TokenExchangeOptions
        {
            Actors =
            [
                Actor(actor =>
                {
                    actor.AllowSecrets = true;
                    actor.Restrictions = [ActorRestrictions.NoSecretReveal];
                }),
            ],
        });

        result.Failures.ShouldBe([$"{Section}:Actors:0:AllowSecrets is true while {Section}:Actors:0:Restrictions names NoSecretReveal; set one of them."]);
    }

    [Fact]
    public void Validate_AdditionalIssuers_ApplyToAuthKitOnly()
    {
        var options = new TokenExchangeOptions { AdditionalIssuers = [Issuer()] };

        Validate(options, user: AuthKitUser()).Succeeded.ShouldBeTrue();
        Validate(options).Failures.ShouldBe([$"{Section}:AdditionalIssuers applies to AuthKit only (Auth:Provider is EntraId)."]);
    }

    [Theory]
    [InlineData(AuthKitIssuer)]
    [InlineData("https://api.workos.com")]
    [InlineData("https://API.workos.com/")]
    public void Validate_AdditionalIssuer_DiffersFromTheAuthKitIssuer(string issuer)
    {
        var result = Validate(new TokenExchangeOptions { AdditionalIssuers = [Issuer(issuer)] }, user: AuthKitUser());

        result.Failures.ShouldBe([$"{Section}:AdditionalIssuers:0:Issuer must differ from Auth:AuthKit:Issuer."]);
    }

    [Fact]
    public void Validate_AdditionalIssuers_AreHttpsAndListedOnce()
    {
        var result = Validate(
            new TokenExchangeOptions
            {
                AdditionalIssuers =
                [
                    Issuer(),
                    Issuer($"{AuthKitDomain}/"),
                    new TokenExchangeIssuerOptions { Issuer = "http://auth.example.org", JwksUri = "auth.example.org/oauth2/jwks" },
                ],
            },
            user: AuthKitUser());

        result.Failures.ShouldBe(
        [
            $"{Section}:AdditionalIssuers:1:Issuer is listed more than once.",
            $"{Section}:AdditionalIssuers:2:Issuer must be an absolute HTTPS URL.",
            $"{Section}:AdditionalIssuers:2:JwksUri must be an absolute HTTPS URL.",
        ]);
    }

    [Fact]
    public void GetScopes_ConfiguredScopes_ReplaceTheDefault()
    {
        Actor().GetScopes().ShouldBe([UserScopes.Impersonation]);
        Actor(actor => actor.Scopes = ["User.Impersonation", "Annotation.Read", "User.Impersonation"])
            .GetScopes().ShouldBe(["User.Impersonation", "Annotation.Read"]);
    }

    [Fact]
    public void GetRestrictions_AddNoSecretRevealUnlessSecretsAreAllowed()
    {
        Actor().GetRestrictions().ShouldBe([ActorRestrictions.NoSecretReveal]);
        Actor(actor => actor.Restrictions = [ActorRestrictions.NoSecretReveal]).GetRestrictions().ShouldBe([ActorRestrictions.NoSecretReveal]);
        Actor(actor => actor.AllowSecrets = true).GetRestrictions().ShouldBeEmpty();
    }

    // A token for an actor's audience is for the exchange only, compared as the start check compares audiences.
    [Theory]
    [InlineData("https://mcp.example.com/mcp")]
    [InlineData("https://mcp.example.com/mcp/")]
    [InlineData("HTTPS://MCP.EXAMPLE.COM/MCP")]
    [InlineData(" https://mcp.example.com/mcp ")]
    [InlineData("6C0F1A2E-1F43-4B0E-9A4D-2F5E8F0C9B12")]
    public void IsIssuedToAnActor_AnAudienceOfAnActor_IsTrue(string audience)
    {
        var options = new TokenExchangeOptions
        {
            Actors =
            [
                new TokenExchangeActorOptions { Id = "other", SubjectAudiences = ["https://other.example.com"] },
                new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example.com/mcp/", "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b12"] },
            ],
        };

        options.IsIssuedToAnActor([$"api://{ApiClientId}", audience]).ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://mcp.example.com/mcp/other")]
    [InlineData("https://mcp.example.com")]
    [InlineData("")]
    [InlineData("/")]
    public void IsIssuedToAnActor_AnotherAudience_IsFalse(string audience)
    {
        var options = new TokenExchangeOptions
        {
            Actors = [new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example.com/mcp", " ", "/"] }],
        };

        options.IsIssuedToAnActor([audience]).ShouldBeFalse();
        new TokenExchangeOptions().IsIssuedToAnActor(["https://mcp.example.com/mcp"]).ShouldBeFalse();
    }

    // Whether the exchange is on or not: actors' tokens are never bearer tokens of the API.
    [Fact]
    public void IsIssuedToAnActor_WhileTheExchangeIsDisabled_IsTrue()
    {
        var options = new TokenExchangeOptions
        {
            Enabled = false,
            Actors = [new TokenExchangeActorOptions { Id = "mcp", SubjectAudiences = ["https://mcp.example.com/mcp"] }],
        };

        options.IsIssuedToAnActor(["https://mcp.example.com/mcp"]).ShouldBeTrue();
    }

    private static ValidateOptionsResult Validate(
        TokenExchangeOptions options,
        string environment = "Production",
        UserAuthenticationOptions? user = null,
        FederationOptions? federation = null)
    {
        var validator = new TokenExchangeOptionsValidator(
            new TestHostEnvironment(environment),
            Options.Create(user ?? EntraUser()),
            Options.Create(federation ?? new FederationOptions { PublicBaseUrl = PublicBaseUrl }));
        return validator.Validate(null, options);
    }

    private static TokenExchangeActorOptions Actor(Action<TokenExchangeActorOptions>? configure = null)
    {
        var actor = new TokenExchangeActorOptions
        {
            Id = "mcp",
            Authentication = new ActorAuthenticationOptions
            {
                WorkloadIdentity = new ActorWorkloadIdentityOptions
                {
                    Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0",
                    Subject = "managed-identity-oid",
                    Audience = "api://storyteller-token-exchange",
                },
            },
            SubjectAudiences = ["https://mcp.example.com/mcp"],
        };

        configure?.Invoke(actor);
        return actor;
    }

    private static TokenExchangeActorOptions SecretActor(string hash, string id = "mcp")
    {
        return Actor(actor =>
        {
            actor.Id = id;
            actor.Authentication = new ActorAuthenticationOptions { ClientSecretHash = hash };
        });
    }

    private static TokenExchangeActorOptions AssertionActor()
    {
        return Actor(actor => actor.Authentication = new ActorAuthenticationOptions
        {
            ClientAssertion = new ActorClientAssertionOptions { JwksUri = "https://mcp.example.com/.well-known/jwks.json" },
        });
    }

    private static TokenExchangeIssuerOptions Issuer(string issuer = AuthKitDomain)
    {
        return new TokenExchangeIssuerOptions { Issuer = issuer, JwksUri = $"{AuthKitDomain}/oauth2/jwks" };
    }

    private static UserAuthenticationOptions EntraUser()
    {
        return new UserAuthenticationOptions { Provider = IdentityProviderKind.EntraId, ClientId = ApiClientId, TenantId = TenantId };
    }

    private static UserAuthenticationOptions AuthKitUser()
    {
        return new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions { ClientId = "client_123", Issuer = AuthKitIssuer },
        };
    }

    private static Dictionary<string, string?> EntraSettings(Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:ClientId"] = ApiClientId,
            ["Auth:TenantId"] = TenantId,
        };

        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        return values;
    }

    private static TokenExchangeOptions Resolve(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddFederationOptions(configuration);
        services.AddUserAuthenticationOptions(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TokenExchangeOptions>>().Value;
    }

    private sealed class ThrowingOptions<TOptions> : IOptions<TOptions>
        where TOptions : class
    {
        public TOptions Value => throw new InvalidOperationException($"The {typeof(TOptions).Name} were read.");
    }
}
