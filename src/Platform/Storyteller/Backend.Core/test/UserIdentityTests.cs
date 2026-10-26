using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class UserIdentityTests
{
    private const string TenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    private const string ObjectId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string MappedTenantId = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string MappedObjectId = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    [Fact]
    public void GetAccountId_EntraRawClaims_IsTenantAndObjectId()
    {
        var accountId = UserIdentity.GetAccountId(
            [new Claim("sub", "pairwise-sub"), new Claim("tid", TenantId), new Claim("oid", ObjectId)],
            IdentityProviderKind.EntraId);

        accountId.ShouldBe($"{TenantId}:{ObjectId}");
    }

    // JwtSecurityTokenHandler maps tid and oid on the validated path; sub becomes NameIdentifier and is not used.
    [Fact]
    public void GetAccountId_EntraMappedClaims_IsTenantAndObjectId()
    {
        var accountId = UserIdentity.GetAccountId(
            [new Claim(ClaimTypes.NameIdentifier, "pairwise-sub"), new Claim(MappedTenantId, TenantId), new Claim(MappedObjectId, ObjectId)],
            IdentityProviderKind.EntraId);

        accountId.ShouldBe($"{TenantId}:{ObjectId}");
    }

    [Theory]
    [InlineData("8A7B6C5D-4E3F-4A1B-8C9D-0E1F2A3B4C5D", "3F2C6A8E-6C1D-4A59-9A47-0A2F4F4C1B2D")]
    [InlineData("{8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d}", "3f2c6a8e6c1d4a599a470a2f4f4c1b2d")]
    [InlineData(" 8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d ", "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d")]
    public void GetAccountId_EntraGuidsInAnotherForm_AreNormalized(string tenantId, string objectId)
    {
        var accountId = UserIdentity.GetAccountId([new Claim("tid", tenantId), new Claim("oid", objectId)], IdentityProviderKind.EntraId);

        accountId.ShouldBe($"{TenantId}:{ObjectId}");
        UserIdentity.IsEntraAccountId(accountId).ShouldBeTrue();
    }

    [Fact]
    public void GetAccountId_EntraRawAndMappedNames_MayBeMixed()
    {
        var accountId = UserIdentity.GetAccountId([new Claim(MappedTenantId, TenantId), new Claim("oid", ObjectId)], IdentityProviderKind.EntraId);

        accountId.ShouldBe($"{TenantId}:{ObjectId}");
    }

    [Theory]
    [InlineData(null, ObjectId)]
    [InlineData(TenantId, null)]
    [InlineData(null, null)]
    [InlineData("", ObjectId)]
    [InlineData(TenantId, " ")]
    [InlineData("tenant-id", ObjectId)]
    [InlineData(TenantId, "user-1")]
    public void GetAccountId_EntraWithoutAGuidTenantAndObjectId_IsNull(string? tenantId, string? objectId)
    {
        List<Claim> claims = [new("sub", "pairwise-sub")];

        if (tenantId is not null)
        {
            claims.Add(new Claim("tid", tenantId));
        }

        if (objectId is not null)
        {
            claims.Add(new Claim("oid", objectId));
        }

        UserIdentity.GetAccountId(claims, IdentityProviderKind.EntraId).ShouldBeNull();
    }

    [Theory]
    [InlineData(IdentityProviderKind.AuthKit)]
    [InlineData(IdentityProviderKind.Keycloak)]
    public void GetAccountId_AuthKitAndKeycloak_IsTheTrimmedSub(IdentityProviderKind provider)
    {
        var accountId = UserIdentity.GetAccountId(
            [new Claim("sub", " user_01 "), new Claim("tid", TenantId), new Claim("oid", ObjectId)],
            provider);

        accountId.ShouldBe("user_01");
    }

    [Theory]
    [InlineData(IdentityProviderKind.AuthKit)]
    [InlineData(IdentityProviderKind.Keycloak)]
    public void GetAccountId_AuthKitAndKeycloak_ReadTheMappedSub(IdentityProviderKind provider)
    {
        UserIdentity.GetAccountId([new Claim(ClaimTypes.NameIdentifier, "user_01")], provider).ShouldBe("user_01");
    }

    [Theory]
    [InlineData(IdentityProviderKind.AuthKit, null)]
    [InlineData(IdentityProviderKind.AuthKit, " ")]
    [InlineData(IdentityProviderKind.Keycloak, null)]
    [InlineData(IdentityProviderKind.Keycloak, "")]
    public void GetAccountId_AuthKitAndKeycloakWithoutSub_IsNull(IdentityProviderKind provider, string? subject)
    {
        List<Claim> claims = [new("name", "Ada")];

        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        UserIdentity.GetAccountId(claims, provider).ShouldBeNull();
    }

    [Fact]
    public void GetAccountId_UnknownProvider_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => UserIdentity.GetAccountId([new Claim("sub", "user-1")], (IdentityProviderKind)42));
    }

    [Theory]
    [InlineData($"{TenantId}:{ObjectId}", true)]
    [InlineData("8A7B6C5D-4E3F-4A1B-8C9D-0E1F2A3B4C5D:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d", false)]
    [InlineData("8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2D", false)]
    [InlineData("{8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c}:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d", false)]
    [InlineData("8a7b6c5d4e3f4a1b8c9d0e1f2a3b4c5d:3f2c6a8e6c1d4a599a470a2f4f4c1b2d", false)]
    [InlineData($"{TenantId}-{ObjectId}", false)]
    [InlineData($"{TenantId}:{ObjectId} ", false)]
    [InlineData(TenantId, false)]
    [InlineData("pairwise-sub", false)]
    [InlineData("user_01", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEntraAccountId_AcceptsOnlyTheLowerCaseGuidPair(string? accountId, bool expected)
    {
        UserIdentity.IsEntraAccountId(accountId).ShouldBe(expected);
    }
}
