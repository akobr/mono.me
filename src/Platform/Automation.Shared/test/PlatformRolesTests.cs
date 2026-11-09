using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Automation.Shared.UnitTests;

// The pool user each Role leases, by the Platform's role names in Users:Pool.
public sealed class PlatformRolesTests
{
    public static TheoryData<Role, string[]> Requirements => new()
    {
        { Role.Any, [] },
        { Role.Editor, ["Owner", "Administrator", "ContributorWithSecrets", "Contributor"] },
        { Role.Admin, ["Owner", "Administrator"] },
        { Role.Secrets, ["Owner", "Administrator", "ContributorWithSecrets"] },
        { Role.Owner, ["Owner"] },
        { Role.Contributor, ["Contributor"] },
        { Role.Reader, ["Reader"] },
    };

    [Theory]
    [MemberData(nameof(Requirements))]
    public void RequirementOf_NamesThePoolRoles_OfEachRole(Role role, string[] roles)
    {
        PlatformRoles.RequirementOf(role).Roles.ShouldBe(roles, ignoreOrder: true);
    }

    [Fact]
    public void RequirementOf_CoversEveryRole()
    {
        Enum.GetValues<Role>().ShouldBe(Requirements.Select(row => row.Data.Item1), ignoreOrder: true);
    }

    [Fact]
    public void AnExactRole_IsNotSatisfiedByAHigherOne()
    {
        var owner = new TestUser(0, "owner@example.test", roles: [PlatformRoles.Owner]);
        var reader = new TestUser(1, "reader@example.test", roles: [PlatformRoles.Reader]);

        PlatformRoles.RequirementOf(Role.Reader).IsSatisfiedBy(owner).ShouldBeFalse();
        PlatformRoles.RequirementOf(Role.Reader).IsSatisfiedBy(reader).ShouldBeTrue();
        PlatformRoles.RequirementOf(Role.Editor).IsSatisfiedBy(owner).ShouldBeTrue();
        PlatformRoles.RequirementOf(Role.Editor).IsSatisfiedBy(reader).ShouldBeFalse();
        PlatformRoles.RequirementOf(Role.Any).IsSatisfiedBy(reader).ShouldBeTrue();
    }
}
