namespace _42.Platform.Automation.Shared.UnitTests;

// [UserRole] of a test: the method's wins over its class's, a base class's counts, and a test without one is Any.
public sealed class UserRoleAttributeTests
{
    [Fact]
    public void Of_TakesTheMethodsRole_OverTheClasss()
    {
        UserRoleAttribute.Of(typeof(ReaderTests), nameof(ReaderTests.Edit)).ShouldBe(Role.Editor);
    }

    [Fact]
    public void Of_TakesTheClasssRole_ForAMethodWithoutOne_AnUnknownMethod_OrNoMethod()
    {
        UserRoleAttribute.Of(typeof(ReaderTests), nameof(ReaderTests.Read)).ShouldBe(Role.Reader);
        UserRoleAttribute.Of(typeof(ReaderTests), "Missing").ShouldBe(Role.Reader);
        UserRoleAttribute.Of(typeof(ReaderTests), null).ShouldBe(Role.Reader);
    }

    [Fact]
    public void Of_TakesTheRoleOfABaseClass_AndOfAnInheritedMethod()
    {
        UserRoleAttribute.Of(typeof(DerivedReaderTests), nameof(ReaderTests.Read)).ShouldBe(Role.Reader);
        UserRoleAttribute.Of(typeof(DerivedReaderTests), nameof(ReaderTests.Edit)).ShouldBe(Role.Editor);
        UserRoleAttribute.Of(typeof(DerivedReaderTests), nameof(DerivedReaderTests.Administer)).ShouldBe(Role.Admin);
    }

    [Fact]
    public void Of_IsAny_WithoutAnAttribute()
    {
        UserRoleAttribute.Of(typeof(PlainTests), nameof(PlainTests.Run)).ShouldBe(Role.Any);
        UserRoleAttribute.Of(typeof(PlainTests), null).ShouldBe(Role.Any);
    }

    [Fact]
    public void Of_FindsTheRole_OfAnyOverloadOfTheName()
    {
        UserRoleAttribute.Of(typeof(PlainTests), nameof(PlainTests.Overloaded)).ShouldBe(Role.Owner);
    }

    [UserRole(Role.Reader)]
    private class ReaderTests
    {
        public void Read()
        {
        }

        [UserRole(Role.Editor)]
        public void Edit()
        {
        }
    }

    private sealed class DerivedReaderTests : ReaderTests
    {
        [UserRole(Role.Admin)]
        public void Administer()
        {
        }
    }

    private sealed class PlainTests
    {
        public void Run()
        {
        }

        public void Overloaded()
        {
        }

        [UserRole(Role.Owner)]
        public void Overloaded(int value)
        {
            _ = value;
        }
    }
}
