namespace _42.Testing.UI.Automation.Runtime.TUnit.UnitTests;

public sealed class TestIdentityFactoryTests
{
    [Fact]
    public void CreateId_IsAssemblyClassAndMethod()
    {
        TestIdentityFactory.CreateId("42.Sample", "_42.Sample.Tests.SignInTests", "SignIn", "SignIn")
            .ShouldBe("42.Sample/_42.Sample.Tests.SignInTests/SignIn");
        TestIdentityFactory.CreateId("42.Sample", "_42.Sample.Tests.SignInTests", "SignIn", null)
            .ShouldBe("42.Sample/_42.Sample.Tests.SignInTests/SignIn");
    }

    [Fact]
    public void CreateId_AddsTheDisplayNameOfADataDrivenCase()
    {
        TestIdentityFactory.CreateId("42.Sample", "_42.Sample.DataTests", "Data", "Data(1, one)")
            .ShouldBe("42.Sample/_42.Sample.DataTests/Data/Data(1, one)");
    }

    [Fact]
    public void CreateId_AddsAHashOfTheInstance_ForATestThatRunsAsSeveral()
    {
        // TUnit's TestIds of two repetitions, and of two rows whose arguments format the same.
        var first = TestIdentityFactory.CreateId("42.Sample", "_42.Sample.DataTests", "Data", "Data", "_42.Sample.DataTests.1.1.Data.1.1.0");
        var second = TestIdentityFactory.CreateId("42.Sample", "_42.Sample.DataTests", "Data", "Data", "_42.Sample.DataTests.1.1.Data.1.1.1");
        var row = TestIdentityFactory.CreateId("42.Sample", "_42.Sample.DataTests", "Data", "Data(Item)", "_42.Sample.DataTests.1.1.Data(Item).1.1.0");
        var sameText = TestIdentityFactory.CreateId("42.Sample", "_42.Sample.DataTests", "Data", "Data(Item)", "_42.Sample.DataTests.1.1.Data(Item).2.1.0");

        first.ShouldMatch(@"^42\.Sample/_42\.Sample\.DataTests/Data#[0-9a-f]{8}$");
        row.ShouldMatch(@"^42\.Sample/_42\.Sample\.DataTests/Data/Data\(Item\)#[0-9a-f]{8}$");
        new[] { first, second, row, sameText }.Distinct().Count().ShouldBe(4);
        TestIdentityFactory.CreateId("42.Sample", "_42.Sample.DataTests", "Data", "Data", "_42.Sample.DataTests.1.1.Data.1.1.0").ShouldBe(first);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(2, 0, 0, true)]
    [InlineData(0, 1, 0, true)]
    [InlineData(0, 0, 3, true)]
    public void IsOneOfSeveral_IsADataRowOrARepetition(int methodArguments, int classArguments, int repeats, bool expected)
    {
        TestIdentityFactory.IsOneOfSeveral(methodArguments, classArguments, repeats).ShouldBe(expected);
    }

    [Fact]
    public void FindCases_ReadsTheMethodTheClassAndItsBaseClasses()
    {
        var cases = TestIdentityFactory.FindCases(typeof(LinkedTests), nameof(LinkedTests.Linked));

        cases.ShouldBe(
        [
            new TestCaseLink("12", null),
            new TestCaseLink("TC-34", "Testiny"),
            new TestCaseLink("56", null),
            new TestCaseLink("78", null),
        ],
        ignoreOrder: true);
    }

    [Fact]
    public void FindCases_IsEmpty_WithoutTestCase()
    {
        TestIdentityFactory.FindCases(typeof(TestIdentityFactoryTests), nameof(FindCases_IsEmpty_WithoutTestCase)).ShouldBeEmpty();
    }

    [TestCase("78")]
    public abstract class LinkedTestsBase
    {
    }

    [TestCase("56")]
    public sealed class LinkedTests : LinkedTestsBase
    {
        [TestCase("12")]
        [TestCase("TC-34", System = "Testiny")]
        [TestCase("12")]
        public void Linked()
        {
        }
    }
}
