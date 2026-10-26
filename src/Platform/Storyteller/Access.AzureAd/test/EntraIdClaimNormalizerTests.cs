using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class EntraIdClaimNormalizerTests
{
    private const string McpClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11";
    private const string McpTenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    private const string TenantId = "0f1e2d3c-4b5a-4968-8776-655443322110";
    private const string ObjectId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string AccountId = $"{TenantId}:{ObjectId}";
    private const string MappedTenantId = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string MappedObjectId = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    private readonly EntraIdClaimNormalizer _normalizer = new(Options.Create(new UserAuthenticationOptions
    {
        Provider = IdentityProviderKind.EntraId,
        TenantId = "tenant-id",
        ClientId = "client-id",
    }));

    // Decoded (DEV_AUTH) claims are raw: sub, tid and oid.
    [Fact]
    public void Normalize_AzpMatchingTheApiClient_IsAUserKeyedByTenantAndObjectId()
    {
        List<Claim> claims = [new("sub", "user-1"), new("azp", "CLIENT-ID"), new("tid", TenantId), new("oid", ObjectId), new("name", "Ada")];

        var result = _normalizer.Normalize(claims);

        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
        result.Claims.Where(claim => claim.Type == "sub").ShouldHaveSingleItem().Value.ShouldBe(AccountId);
        result.Claims.Where(claim => claim.Type == UserIdentity.EntraSubjectClaimType).ShouldHaveSingleItem().Value.ShouldBe("user-1");
        result.Claims.ShouldContain(claim => claim.Type == "name" && claim.Value == "Ada");
        result.Claims.ShouldContain(claim => claim.Type == "tid" && claim.Value == TenantId);
        result.Claims.ShouldContain(claim => claim.Type == "oid" && claim.Value == ObjectId);
    }

    // Claims of an authenticated identity (for example EasyAuth) come mapped: NameIdentifier and the schema URIs.
    [Fact]
    public void Normalize_MappedClaimTypes_AreKeyedToo()
    {
        var result = _normalizer.Normalize(
            [new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(MappedTenantId, TenantId), new Claim(MappedObjectId, ObjectId)]);

        result.IsMachine.ShouldBeFalse();
        result.Claims.Where(claim => claim.Type == "sub").ShouldHaveSingleItem().Value.ShouldBe(AccountId);
        result.Claims.ShouldNotContain(claim => claim.Type == ClaimTypes.NameIdentifier);
        result.Claims.Where(claim => claim.Type == UserIdentity.EntraSubjectClaimType).ShouldHaveSingleItem().Value.ShouldBe("user-1");
    }

    [Fact]
    public void Normalize_UpperCaseTenantAndObjectId_AreKeyedInLowerCase()
    {
        var result = _normalizer.Normalize(
            [new Claim("sub", "user-1"), new Claim("tid", TenantId.ToUpperInvariant()), new Claim("oid", ObjectId.ToUpperInvariant())]);

        result.Claims.Where(claim => claim.Type == "sub").ShouldHaveSingleItem().Value.ShouldBe(AccountId);
    }

    // The normalizer always gives a result; a user without an account id keeps no sub, so GetIdentityUniqueId is 401.
    [Theory]
    [InlineData(null, ObjectId)]
    [InlineData(TenantId, null)]
    [InlineData("tenant-id", ObjectId)]
    [InlineData(TenantId, "user-1")]
    public void Normalize_UserWithoutAGuidTenantAndObjectId_KeepsNoSub(string? tenantId, string? objectId)
    {
        List<Claim> claims = [new("sub", "user-1"), new(ClaimTypes.NameIdentifier, "user-1"), new("name", "Ada")];

        if (tenantId is not null)
        {
            claims.Add(new Claim("tid", tenantId));
        }

        if (objectId is not null)
        {
            claims.Add(new Claim("oid", objectId));
        }

        var result = _normalizer.Normalize(claims);

        result.IsMachine.ShouldBeFalse();
        result.Claims.ShouldNotContain(claim => claim.Type == "sub" || claim.Type == ClaimTypes.NameIdentifier);
        result.Claims.ShouldContain(claim => claim.Type == "name");
    }

    // Machines keep the claims as they are: the same list, with their sub and without tid or oid.
    [Fact]
    public void Normalize_OtherAzp_IsAMachineWithTheSameClaims()
    {
        List<Claim> claims = [new("sub", "machine-app"), new("azp", "machine-app")];

        var result = _normalizer.Normalize(claims);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-app");
        result.Claims.ShouldBeSameAs(claims);
    }

    // A delegated token (scp) of another client app is a machine, as on the validated path.
    [Fact]
    public void Normalize_DelegatedTokenOfAnotherClientApp_IsAMachine()
    {
        List<Claim> claims = [new("sub", "user-1"), new("azp", McpClientId), new("tid", McpTenantId), new("oid", ObjectId), new("scp", "User.Impersonation")];

        var result = _normalizer.Normalize(claims);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe(McpClientId);
        result.Claims.ShouldBeSameAs(claims);
    }
}
