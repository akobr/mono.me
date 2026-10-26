using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

public class KeycloakClaimNormalizerTests
{
    private const string UserId = "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b";
    private const string JsonValueType = "JSON";

    [Fact]
    public void NormalizeUser_MapsRealmAndClientRoles_AfterTheDefaults()
    {
        var normalizer = Create(
            defaultScopes: ["User.Impersonation"],
            roleMap: new Dictionary<string, string>
            {
                ["storyteller-admin"] = "Annotation.Write",
                ["storyteller-api:annotation-reader"] = "Annotation.Read, User.Impersonation",
                ["storyteller:2s:reader"] = "Configuration.Read",
            });

        var result = normalizer.NormalizeUser(
        [
            new Claim("sub", UserId),
            Json("realm_access", """{"roles":["default-roles-storyteller","offline_access","storyteller-admin","storyteller:2s:reader"]}"""),
            Json("resource_access", """{"storyteller-api":{"roles":["annotation-reader"]},"account":{"roles":["manage-account"]}}"""),
        ]);

        result.IsMachine.ShouldBeFalse();
        Values(result, "scp").ShouldBe(["User.Impersonation Annotation.Write Configuration.Read Annotation.Read"]);
        Values(result, "sub").ShouldBe([UserId]);
    }

    // Client roles are named by their client, so a role of the same name in another client grants nothing.
    [Fact]
    public void NormalizeUser_ClientRoleOfAnotherClient_GrantsNothing()
    {
        var normalizer = Create(roleMap: new Dictionary<string, string>
        {
            ["storyteller-api:admin"] = "Annotation.Write",
            ["admin"] = "Configuration.Write",
        });

        var result = normalizer.NormalizeUser(
        [
            new Claim("sub", UserId),
            Json("resource_access", """{"other-app":{"roles":["admin"]}}"""),
        ]);

        Values(result, "scp").ShouldBeEmpty();
    }

    [Fact]
    public void NormalizeUser_WithoutRoles_GetsTheDefaultsOnly()
    {
        var normalizer = Create(defaultScopes: ["User.Impersonation"]);

        var result = normalizer.NormalizeUser([new Claim("sub", UserId)]);

        Values(result, "scp").ShouldBe(["User.Impersonation"]);
    }

    [Fact]
    public void NormalizeUser_WithoutDefaultsOrMappedRoles_HasNoScp()
    {
        var normalizer = Create(roleMap: new Dictionary<string, string> { ["mapped"] = "Annotation.Read" });

        var result = normalizer.NormalizeUser(
        [
            new Claim("sub", UserId),
            Json("realm_access", """{"roles":["unmapped"]}"""),
        ]);

        Values(result, "scp").ShouldBeEmpty();
    }

    // Scope-bearing claims are rebuilt from configuration only: a token mapper cannot grant scopes.
    [Fact]
    public void NormalizeUser_DropsTheScopeBearingClaimsOfTheToken()
    {
        var normalizer = Create(defaultScopes: ["User.Impersonation"]);

        var result = normalizer.NormalizeUser(
        [
            new Claim("sub", UserId),
            new Claim("scp", "Secrets.Read"),
            new Claim("roles", "Configuration.Write"),
            new Claim(ClaimTypes.Role, "Annotation.Write"),
            new Claim("http://schemas.microsoft.com/identity/claims/scope", "Annotation.Read"),
            new Claim("scope", "openid profile email"),
            new Claim("azp", "sform"),
        ]);

        Values(result, "scp").ShouldBe(["User.Impersonation"]);
        Values(result, "roles").ShouldBeEmpty();
        Values(result, ClaimTypes.Role).ShouldBeEmpty();
        Values(result, "http://schemas.microsoft.com/identity/claims/scope").ShouldBeEmpty();
        Values(result, "scope").ShouldBe(["openid profile email"]);
        Values(result, "azp").ShouldBe(["sform"]);
    }

    [Theory]
    [InlineData("realm_access", "not json")]
    [InlineData("realm_access", """["admin"]""")]
    [InlineData("realm_access", """{"roles":"admin"}""")]
    [InlineData("realm_access", """{"roles":[42, null, ""]}""")]
    [InlineData("resource_access", """{"storyteller-api":["admin"]}""")]
    [InlineData("resource_access", """["admin"]""")]
    public void NormalizeUser_RoleClaimsOfAnotherShape_GrantNothing(string claimType, string value)
    {
        var normalizer = Create(roleMap: new Dictionary<string, string>
        {
            ["admin"] = "Annotation.Write",
            ["storyteller-api:admin"] = "Annotation.Write",
        });

        var result = normalizer.NormalizeUser([new Claim("sub", UserId), Json(claimType, value)]);

        Values(result, "scp").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Ada Lovelace", "Ada", "Lovelace", "ada", "ada@example.com", "Ada Lovelace")]
    [InlineData(null, "Ada", "Lovelace", "ada", "ada@example.com", "Ada Lovelace")]
    [InlineData(" ", "Ada", " ", "ada", "ada@example.com", "Ada")]
    [InlineData(null, null, null, "ada", "ada@example.com", "ada")]
    [InlineData(null, null, null, null, "ada@example.com", "ada@example.com")]
    public void NormalizeUser_Name_FallsBackThroughTheGivenAndFamilyNameTheUserNameAndTheEmail(
        string? name,
        string? givenName,
        string? familyName,
        string? userName,
        string? email,
        string expected)
    {
        var normalizer = Create();
        var claims = new List<Claim> { new("sub", UserId) };
        Add(claims, "name", name);
        Add(claims, "given_name", givenName);
        Add(claims, "family_name", familyName);
        Add(claims, "preferred_username", userName);
        Add(claims, "email", email);

        var result = normalizer.NormalizeUser(claims);

        Values(result, "name").ShouldBe([expected]);
    }

    [Theory]
    [InlineData("ada", "ada@example.com", "ada")]
    [InlineData(null, "ada@example.com", "ada@example.com")]
    [InlineData(" ", "ada@example.com", "ada@example.com")]
    public void NormalizeUser_PreferredUserName_FallsBackToTheEmail(string? userName, string email, string expected)
    {
        var normalizer = Create();
        var claims = new List<Claim> { new("sub", UserId), new("email", email) };
        Add(claims, "preferred_username", userName);

        var result = normalizer.NormalizeUser(claims);

        Values(result, "preferred_username").ShouldBe([expected]);
        Values(result, "email").ShouldBe([email]);
    }

    [Fact]
    public void NormalizeUser_WithoutAnyName_AddsNone()
    {
        var result = Create().NormalizeUser([new Claim("sub", UserId)]);

        Values(result, "name").ShouldBeEmpty();
        Values(result, "preferred_username").ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_UserClaims_AreAUser()
    {
        var normalizer = Create(defaultScopes: ["User.Impersonation"]);

        var result = normalizer.Normalize([new Claim("sub", UserId), new Claim("azp", "sform"), new Claim("preferred_username", "ada")]);

        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
        Values(result, "sub").ShouldBe([UserId]);
        Values(result, "scp").ShouldBe(["User.Impersonation"]);
    }

    // DEV_AUTH decodes tokens without validation: a service account is the machine of its client, as the machine
    // validator has it, with the scopes of its storyteller_scope mapper and no user defaults.
    [Theory]
    [InlineData("client_id", null)]
    [InlineData("clientId", null)]
    [InlineData(null, "service-account-42.sform.org1.proj1.abc")]
    public void Normalize_ServiceAccount_IsTheMachineOfItsClient(string? clientIdClaimType, string? userName)
    {
        var normalizer = Create(defaultScopes: ["User.Impersonation"]);
        var claims = new List<Claim>
        {
            new("sub", "b0c1d2e3-service-account"),
            new("azp", "42.sform.org1.proj1.abc"),
            new("storyteller_scope", "Annotation.Read"),
            new("scp", "Secrets.Read"),
        };
        Add(claims, clientIdClaimType, "42.sform.org1.proj1.abc");
        Add(claims, "preferred_username", userName);

        var result = normalizer.Normalize(claims);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("42.sform.org1.proj1.abc");
        Values(result, "scp").ShouldBe(["Annotation.Read"]);
        Values(result, "azp").ShouldBe(["42.sform.org1.proj1.abc"]);
    }

    [Fact]
    public void Normalize_ServiceAccountWithoutAzp_IsTheMachineNamedByClientId()
    {
        var result = Create().Normalize([new Claim("sub", "b0c1d2e3"), new Claim("client_id", "machine-client")]);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-client");
    }

    // A service account that names no client is neither a machine nor a person: it keeps no sub, so
    // GetIdentityUniqueId answers 401.
    [Fact]
    public void Normalize_ServiceAccountWithoutAnyClient_KeepsNoSub()
    {
        var normalizer = Create(defaultScopes: ["User.Impersonation"]);

        var result = normalizer.Normalize(
        [
            new Claim("sub", "b0c1d2e3"),
            new Claim(ClaimTypes.NameIdentifier, "b0c1d2e3"),
            new Claim("preferred_username", "service-account-robot"),
        ]);

        result.IsMachine.ShouldBeFalse();
        Values(result, "sub").ShouldBeEmpty();
        Values(result, ClaimTypes.NameIdentifier).ShouldBeEmpty();
        UserIdentity.GetAccountId(result.Claims, IdentityProviderKind.Keycloak).ShouldBeNull();
    }

    [Theory]
    [InlineData("client_id", "machine", true)]
    [InlineData("clientId", "machine", true)]
    [InlineData("preferred_username", "service-account-machine", true)]
    [InlineData("preferred_username", "SERVICE-ACCOUNT-machine", true)]
    [InlineData("preferred_username", "ada", false)]
    [InlineData("azp", "sform", false)]
    public void IsServiceAccount_ByTheClientIdClaimsOrTheUserName(string claimType, string value, bool expected)
    {
        KeycloakClaimNormalizer.IsServiceAccount([new Claim("sub", UserId), new Claim(claimType, value)]).ShouldBe(expected);
    }

    private static KeycloakClaimNormalizer Create(string[]? defaultScopes = null, Dictionary<string, string>? roleMap = null)
    {
        return new KeycloakClaimNormalizer(Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions
            {
                DefaultUserScopes = defaultScopes ?? [],
                RoleMap = roleMap ?? new Dictionary<string, string>(),
            },
        }));
    }

    // A JSON object claim, the way both token handlers hand it over.
    private static Claim Json(string type, string value)
    {
        return new Claim(type, value, JsonValueType);
    }

    private static void Add(List<Claim> claims, string? type, string? value)
    {
        if (type is not null && value is not null)
        {
            claims.Add(new Claim(type, value));
        }
    }

    private static string[] Values(BearerValidationResult result, string type)
    {
        return result.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();
    }
}
