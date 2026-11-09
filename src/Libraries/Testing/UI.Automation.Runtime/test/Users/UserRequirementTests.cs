using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Users;

public sealed class UserRequirementTests
{
    private static readonly TestUser Owner = new(0, "owner@example.test", roles: ["Owner"]);
    private static readonly TestUser Reader = new(1, "reader@example.test", roles: ["Reader"]);
    private static readonly TestUser Unregistered = new(2, "new@example.test", roles: ["Reader", "Registration"]);
    private static readonly TestUser NoRoles = new(3, "plain@example.test");

    [Fact]
    public void Any_IsSatisfiedByEveryUser()
    {
        UserRequirement.Any.IsSatisfiedBy(Owner).ShouldBeTrue();
        UserRequirement.Any.IsSatisfiedBy(Unregistered).ShouldBeTrue();
        UserRequirement.Any.IsSatisfiedBy(NoRoles).ShouldBeTrue();
        UserRequirement.Any.ExcludedRoles.ShouldBeEmpty();
        UserRequirement.Any.ToString().ShouldBe("any role");
    }

    [Fact]
    public void WithAnyRole_NeedsOneOfTheRoles_WithoutCase()
    {
        var requirement = UserRequirement.WithAnyRole("owner", "Administrator");

        requirement.IsSatisfiedBy(Owner).ShouldBeTrue();
        requirement.IsSatisfiedBy(Reader).ShouldBeFalse();
        requirement.ToString().ShouldBe("role owner or Administrator");
    }

    [Fact]
    public void Except_RefusesAUserWithAnExcludedRole()
    {
        var requirement = UserRequirement.Any.Except("Registration", "MultiOrganization");

        requirement.IsSatisfiedBy(Owner).ShouldBeTrue();
        requirement.IsSatisfiedBy(NoRoles).ShouldBeTrue();
        requirement.IsSatisfiedBy(Unregistered).ShouldBeFalse();
        requirement.Roles.ShouldBeEmpty();
        requirement.ExcludedRoles.ShouldBe(["Registration", "MultiOrganization"]);
        requirement.ToString().ShouldBe("any role and none of Registration, MultiOrganization");
    }

    [Fact]
    public void Except_KeepsTheRolesOfTheRequirement()
    {
        var requirement = UserRequirement.WithRole("Reader").Except("registration");

        requirement.IsSatisfiedBy(Reader).ShouldBeTrue();
        requirement.IsSatisfiedBy(Unregistered).ShouldBeFalse();
        requirement.IsSatisfiedBy(Owner).ShouldBeFalse();
        requirement.Roles.ShouldBe(["Reader"]);
        requirement.ToString().ShouldBe("role Reader and none of registration");
    }

    [Fact]
    public void Except_ReturnsANewRequirement_AndAddsUp()
    {
        var reader = UserRequirement.WithRole("Reader");

        var once = reader.Except("Registration");
        var twice = once.Except("MultiOrganization", " registration ", string.Empty);

        reader.ExcludedRoles.ShouldBeEmpty();
        once.ShouldNotBeSameAs(reader);
        once.ExcludedRoles.ShouldBe(["Registration"]);
        twice.ExcludedRoles.ShouldBe(["Registration", "MultiOrganization"]);
        UserRequirement.Any.ExcludedRoles.ShouldBeEmpty();
    }

    [Fact]
    public void Except_WithNothingNew_ReturnsTheSameRequirement()
    {
        var once = UserRequirement.Any.Except("Registration");

        once.Except().ShouldBeSameAs(once);
        once.Except(" ", "REGISTRATION").ShouldBeSameAs(once);
        UserRequirement.Any.Except().ShouldBeSameAs(UserRequirement.Any);
        Should.Throw<ArgumentNullException>(() => UserRequirement.Any.Except(null!));
    }
}
