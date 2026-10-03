using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitClaimNormalizerTests
{
    [Fact]
    public void Normalize_NameClaim_IsUsedTrimmed()
    {
        var result = Normalize([new("sub", "user_01"), new("name", "  Ada Lovelace "), new("first_name", "Other")]);

        Values(result, "name").ShouldBe(["Ada Lovelace"]);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void Normalize_BlankTemplateName_FallsBackToFirstAndLastName(string templateName)
    {
        var result = Normalize([new("name", templateName), new("first_name", "Ada"), new("last_name", "Lovelace")]);

        Values(result, "name").ShouldBe(["Ada Lovelace"]);
    }

    [Fact]
    public void Normalize_OnlyFirstName_IsTheName()
    {
        var result = Normalize([new("first_name", "Ada"), new("last_name", " ")]);

        Values(result, "name").ShouldBe(["Ada"]);
    }

    [Fact]
    public void Normalize_GivenAndFamilyName_AreTheNextFallback()
    {
        var result = Normalize([new("given_name", "Ada"), new("family_name", "Lovelace"), new("email", "ada@example.com")]);

        Values(result, "name").ShouldBe(["Ada Lovelace"]);
    }

    [Fact]
    public void Normalize_EmailOnly_IsTheNameAndTheUserName()
    {
        var result = Normalize([new("sub", "user_01"), new("email", "ada@example.com")]);

        Values(result, "name").ShouldBe(["ada@example.com"]);
        Values(result, "preferred_username").ShouldBe(["ada@example.com"]);
    }

    [Fact]
    public void Normalize_ExistingPreferredUsername_WinsOverEmail()
    {
        var result = Normalize([new("preferred_username", "ada"), new("email", "ada@example.com")]);

        Values(result, "preferred_username").ShouldBe(["ada"]);
    }

    [Fact]
    public void Normalize_NoProfileClaims_AddsNoNameOrUserName()
    {
        var result = Normalize([new("sub", "user_01")]);

        Values(result, "name").ShouldBeEmpty();
        Values(result, "preferred_username").ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_DefaultScopesAndMappedPermissions_BecomeOneScpClaim()
    {
        var result = Normalize(
            [
                new("sub", "user_01"),
                new("permissions", "storyteller:annotation-read"),
                new("permissions", "storyteller:admin"),
                new("permissions", "not-mapped"),
            ],
            defaultScopes: ["User.Impersonation", "Annotation.Read"],
            permissionMap: new Dictionary<string, string>
            {
                ["storyteller:annotation-read"] = "Annotation.Read",
                ["storyteller:admin"] = "Default.ReadWrite  Configuration.Secrets",
            });

        Values(result, "scp").ShouldBe(["User.Impersonation Annotation.Read Default.ReadWrite Configuration.Secrets"]);
    }

    [Fact]
    public void Normalize_NoScopes_AddsNoScpClaim()
    {
        var result = Normalize([new("sub", "user_01"), new("permissions", "not-mapped")]);

        Values(result, "scp").ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_PermissionLookup_IsCaseSensitive()
    {
        var result = Normalize(
            [new("permissions", "Storyteller:Annotation-Read")],
            permissionMap: new Dictionary<string, string> { ["storyteller:annotation-read"] = "Annotation.Read" });

        Values(result, "scp").ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_IncomingScopeAndMachineClaims_AreDropped()
    {
        var result = Normalize(
            [
                new("sub", "user_01"),
                new("scp", "Configuration.Secrets"),
                new("roles", "Default.ReadWrite"),
                new(ClaimTypes.Role, "Default.ReadWrite"),
                new("http://schemas.microsoft.com/identity/claims/scope", "Configuration.Secrets"),
                new("azp", "client_01"),
            ],
            defaultScopes: ["User.Impersonation"]);

        Values(result, "scp").ShouldBe(["User.Impersonation"]);
        Values(result, "roles").ShouldBeEmpty();
        Values(result, ClaimTypes.Role).ShouldBeEmpty();
        Values(result, "http://schemas.microsoft.com/identity/claims/scope").ShouldBeEmpty();
        Values(result, "azp").ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_OtherClaims_PassThrough()
    {
        var result = Normalize(
            [
                new("sub", "user_01"),
                new("sid", "session_01"),
                new("org_id", "org_01"),
                new("role", "member"),
                new("permissions", "storyteller:annotation-read"),
            ]);

        Values(result, "sub").ShouldBe(["user_01"]);
        Values(result, "sid").ShouldBe(["session_01"]);
        Values(result, "org_id").ShouldBe(["org_01"]);
        Values(result, "role").ShouldBe(["member"]);
        Values(result, "permissions").ShouldBe(["storyteller:annotation-read"]);
    }

    [Fact]
    public void Normalize_UserToken_IsNeverAMachine()
    {
        var result = Normalize([new("sub", "user_01"), new("azp", "client_01"), new("client_id", "client_01")]);

        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
    }

    private static BearerValidationResult Normalize(
        List<Claim> claims,
        string[]? defaultScopes = null,
        Dictionary<string, string>? permissionMap = null)
    {
        var normalizer = new AuthKitClaimNormalizer(Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = "client_123",
                DefaultUserScopes = defaultScopes ?? [],
                PermissionMap = permissionMap ?? new Dictionary<string, string>(),
            },
        }));

        return normalizer.Normalize(claims);
    }

    private static string[] Values(BearerValidationResult result, string type)
    {
        return result.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();
    }
}
