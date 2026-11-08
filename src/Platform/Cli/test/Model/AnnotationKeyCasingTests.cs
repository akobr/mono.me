using _42.Platform.Cli.Model;
using _42.Platform.Storyteller;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Model;

// The API stores an annotation under its key as written: only the type code is canonical, the names keep their case.
public class AnnotationKeyCasingTests
{
    [Theory]
    [InlineData("EXE.Northwind.Billing.Prod", "exe.Northwind.Billing.Prod")]
    [InlineData("Unt.Billing.Export", "unt.Billing.Export")]
    [InlineData("sbt.NorthWind", "sbt.NorthWind")]
    [InlineData("uxe.northwind.billing.prod.export", "uxe.northwind.billing.prod.export")]
    public void WithLowerCaseTypeCode_ChangesOnlyTheTypeCode(string key, string expected)
    {
        AnnotationKey.Parse(key).WithLowerCaseTypeCode().ToString().ShouldBe(expected);
    }
}
