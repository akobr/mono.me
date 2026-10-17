using _42.Platform.Storyteller.Api.Security;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class MachineAnnotationRestrictionTests
{
    [Fact]
    public void None_IsUnrestrictedAndAllowsEverything()
    {
        var restriction = MachineAnnotationRestriction.None;

        restriction.IsRestricted.ShouldBeFalse();
        restriction.Key.ShouldBeNull();
        restriction.Allows("sbt.northwind").ShouldBeTrue();
        restriction.Allows(AnnotationKey.Parse("uxe.northwind.invoicing.prod.export")).ShouldBeTrue();
        Should.NotThrow(restriction.EnsureUnrestricted);
        Should.NotThrow(() => restriction.EnsureAllows(["rst.invoicing", "cnt.contoso.dev"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void From_BlankKey_IsNone(string? stored)
    {
        var restriction = MachineAnnotationRestriction.From(stored);

        restriction.ShouldBeSameAs(MachineAnnotationRestriction.None);
        restriction.IsRestricted.ShouldBeFalse();
        Should.NotThrow(restriction.EnsureUnrestricted);
    }

    // Without a restriction even a key that does not parse is not refused here; the handler answers it with 400.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("my-annotation-key")]
    [InlineData("sbt.a.b")]
    public void None_AllowsMissingAndInvalidRequestedKeys(string? requested)
    {
        var restriction = MachineAnnotationRestriction.From(null);

        restriction.Allows(requested).ShouldBeTrue();
        Should.NotThrow(() => restriction.EnsureAllows([requested]));
    }

    [Fact]
    public void From_ValidKey_IsRestrictedAndKeepsTheStoredText()
    {
        var restriction = MachineAnnotationRestriction.From("SBT..northwind");

        restriction.IsRestricted.ShouldBeTrue();
        restriction.Key.ShouldBe("SBT..northwind");
    }

    [Theory]
    [InlineData("rst.invoicing")]
    [InlineData("sbt.northwind")]
    [InlineData("unt.invoicing.export")]
    [InlineData("usg.northwind.invoicing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData("exe.northwind.invoicing.prod")]
    [InlineData("uxe.northwind.invoicing.prod.export")]
    public void Restricted_AllowsTheAnnotationItself(string key)
    {
        var restriction = MachineAnnotationRestriction.From(key);

        restriction.Allows(key).ShouldBeTrue();
        restriction.Allows(AnnotationKey.Parse(key)).ShouldBeTrue();
        Should.NotThrow(() => restriction.EnsureAllows([key]));
    }

    [Theory]
    [InlineData("rst.invoicing", "unt.invoicing.export")]
    [InlineData("rst.invoicing", "usg.northwind.invoicing")]
    [InlineData("rst.invoicing", "usg.contoso.invoicing")]
    [InlineData("rst.invoicing", "exe.northwind.invoicing.prod")]
    [InlineData("rst.invoicing", "uxe.contoso.invoicing.dev.export")]
    [InlineData("sbt.northwind", "usg.northwind.invoicing")]
    [InlineData("sbt.northwind", "cnt.northwind.prod")]
    [InlineData("sbt.northwind", "exe.northwind.invoicing.prod")]
    [InlineData("sbt.northwind", "uxe.northwind.invoicing.prod.export")]
    [InlineData("unt.invoicing.export", "uxe.northwind.invoicing.prod.export")]
    [InlineData("unt.invoicing.export", "uxe.contoso.invoicing.dev.export")]
    [InlineData("usg.northwind.invoicing", "exe.northwind.invoicing.prod")]
    [InlineData("usg.northwind.invoicing", "exe.northwind.invoicing.dev")]
    [InlineData("usg.northwind.invoicing", "uxe.northwind.invoicing.prod.export")]
    [InlineData("cnt.northwind.prod", "exe.northwind.invoicing.prod")]
    [InlineData("cnt.northwind.prod", "exe.northwind.billing.prod")]
    [InlineData("cnt.northwind.prod", "uxe.northwind.billing.prod.import")]
    [InlineData("exe.northwind.invoicing.prod", "uxe.northwind.invoicing.prod.export")]
    [InlineData("exe.northwind.invoicing.prod", "uxe.northwind.invoicing.prod.import")]
    public void Restricted_AllowsDescendants(string root, string requested)
    {
        var restriction = MachineAnnotationRestriction.From(root);

        restriction.Allows(requested).ShouldBeTrue();
        restriction.Allows(AnnotationKey.Parse(requested)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("exe.northwind.invoicing.prod", "usg.northwind.invoicing")]
    [InlineData("exe.northwind.invoicing.prod", "cnt.northwind.prod")]
    [InlineData("exe.northwind.invoicing.prod", "sbt.northwind")]
    [InlineData("exe.northwind.invoicing.prod", "rst.invoicing")]
    [InlineData("uxe.northwind.invoicing.prod.export", "exe.northwind.invoicing.prod")]
    [InlineData("uxe.northwind.invoicing.prod.export", "unt.invoicing.export")]
    [InlineData("unt.invoicing.export", "rst.invoicing")]
    [InlineData("usg.northwind.invoicing", "sbt.northwind")]
    [InlineData("cnt.northwind.prod", "sbt.northwind")]
    public void Restricted_DeniesAncestors(string root, string requested)
    {
        var restriction = MachineAnnotationRestriction.From(root);

        restriction.Allows(requested).ShouldBeFalse();
        restriction.Allows(AnnotationKey.Parse(requested)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("sbt.northwind", "sbt.contoso")]
    [InlineData("sbt.northwind", "cnt.contoso.prod")]
    [InlineData("sbt.northwind", "exe.contoso.invoicing.prod")]
    [InlineData("sbt.northwind", "rst.invoicing")]
    [InlineData("sbt.northwind", "unt.invoicing.export")]
    [InlineData("rst.invoicing", "rst.billing")]
    [InlineData("rst.invoicing", "usg.northwind.billing")]
    [InlineData("rst.invoicing", "sbt.northwind")]
    [InlineData("rst.invoicing", "cnt.northwind.prod")]
    [InlineData("cnt.northwind.prod", "cnt.northwind.dev")]
    [InlineData("cnt.northwind.prod", "exe.northwind.invoicing.dev")]
    [InlineData("cnt.northwind.prod", "usg.northwind.invoicing")]
    [InlineData("usg.northwind.invoicing", "usg.northwind.billing")]
    [InlineData("usg.northwind.invoicing", "exe.contoso.invoicing.prod")]
    [InlineData("usg.northwind.invoicing", "unt.invoicing.export")]
    [InlineData("unt.invoicing.export", "unt.invoicing.import")]
    [InlineData("unt.invoicing.export", "uxe.northwind.invoicing.prod.import")]
    [InlineData("exe.northwind.invoicing.prod", "exe.northwind.invoicing.dev")]
    [InlineData("exe.northwind.invoicing.prod", "exe.northwind.billing.prod")]
    [InlineData("exe.northwind.invoicing.prod", "exe.contoso.invoicing.prod")]
    [InlineData("exe.northwind.invoicing.prod", "uxe.northwind.invoicing.dev.export")]
    [InlineData("exe.northwind.invoicing.prod", "unt.invoicing.export")]
    [InlineData("exe.northwind.invoicing.prod", "cnt.northwind.dev")]
    [InlineData("uxe.northwind.invoicing.prod.export", "uxe.northwind.invoicing.prod.import")]
    public void Restricted_DeniesSiblingsAndOtherBranches(string root, string requested)
    {
        var restriction = MachineAnnotationRestriction.From(root);

        restriction.Allows(requested).ShouldBeFalse();
        restriction.Allows(AnnotationKey.Parse(requested)).ShouldBeFalse();
    }

    // Names are compared as whole segments, not as text prefixes.
    [Theory]
    [InlineData("sbt.north", "sbt.northwind")]
    [InlineData("sbt.north", "cnt.northwind.prod")]
    [InlineData("rst.invoice", "unt.invoicing.export")]
    [InlineData("exe.northwind.invoicing.prod", "uxe.northwind.invoicing.production.export")]
    [InlineData("cnt.northwind.prod", "exe.northwind.invoicing.prod2")]
    public void Restricted_DeniesKeysThatOnlyShareATextPrefix(string root, string requested)
    {
        MachineAnnotationRestriction.From(root).Allows(requested).ShouldBeFalse();
    }

    [Theory]
    [InlineData("EXE.northwind.invoicing.prod", "Exe.northwind.invoicing.prod")]
    [InlineData("exe.northwind.invoicing.prod", "EXE.northwind.invoicing.prod")]
    [InlineData("EXE.northwind.invoicing.prod", "uxe.northwind.invoicing.prod.export")]
    [InlineData("exe.northwind.invoicing.prod", "UXE.northwind.invoicing.prod.export")]
    [InlineData("Sbt.northwind", "cNt.northwind.prod")]
    [InlineData("RST.invoicing", "Unt.invoicing.export")]
    public void Restricted_TypeCodeIsCaseInsensitive(string root, string requested)
    {
        var restriction = MachineAnnotationRestriction.From(root);

        restriction.Allows(requested).ShouldBeTrue();
        restriction.Allows(AnnotationKey.Parse(requested)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("sbt..northwind", "sbt.northwind")]
    [InlineData("sbt.northwind", "sbt..northwind")]
    [InlineData("sbt..northwind", "cnt.northwind..prod")]
    [InlineData(".exe.northwind.invoicing.prod.", "uxe..northwind.invoicing.prod.export")]
    public void Restricted_EmptySegmentsCollapse(string root, string requested)
    {
        MachineAnnotationRestriction.From(root).Allows(requested).ShouldBeTrue();
    }

    [Theory]
    [InlineData("sbt.northwind", "sbt.Northwind")]
    [InlineData("sbt.northwind", "cnt.Northwind.prod")]
    [InlineData("sbt.Northwind", "sbt.northwind")]
    [InlineData("exe.northwind.invoicing.prod", "exe.northwind.invoicing.Prod")]
    [InlineData("exe.northwind.invoicing.prod", "uxe.northwind.Invoicing.prod.export")]
    public void Restricted_NamesStayCaseSensitive(string root, string requested)
    {
        var restriction = MachineAnnotationRestriction.From(root);

        restriction.Allows(requested).ShouldBeFalse();
        restriction.Allows(AnnotationKey.Parse(requested)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("my-annotation-key")]
    [InlineData("sbt")]
    [InlineData("sbt.")]
    [InlineData(".")]
    [InlineData("xyz.northwind")]
    [InlineData("sbt.northwind.prod")]
    [InlineData("exe.northwind.invoicing")]
    [InlineData("uxe.a.b.c.d.e")]
    public void Restricted_InvalidStoredKey_AllowsNothing(string stored)
    {
        var restriction = MachineAnnotationRestriction.From(stored);

        restriction.IsRestricted.ShouldBeTrue();
        restriction.Key.ShouldBe(stored);
        restriction.Allows("sbt.northwind").ShouldBeFalse();
        restriction.Allows("uxe.northwind.invoicing.prod.export").ShouldBeFalse();
        restriction.Allows(AnnotationKey.Parse("rst.invoicing")).ShouldBeFalse();
        restriction.Allows(stored).ShouldBeFalse();
        restriction.Allows((string?)null).ShouldBeFalse();
    }

    [Fact]
    public void Restricted_InvalidStoredKey_EnsureMethodsThrow()
    {
        var restriction = MachineAnnotationRestriction.From("my-annotation-key");

        var unrestricted = ShouldBeRestricted(restriction.EnsureUnrestricted);
        var allows = ShouldBeRestricted(() => restriction.EnsureAllows(["sbt.northwind"]));

        unrestricted.Message.ShouldContain("my-annotation-key");
        allows.Message.ShouldContain("my-annotation-key");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("my-annotation-key")]
    [InlineData("sbt.northwind.prod")]
    [InlineData("xyz.northwind")]
    public void Restricted_DeniesMissingAndInvalidRequestedKeys(string? requested)
    {
        var restriction = MachineAnnotationRestriction.From("sbt.northwind");

        restriction.Allows(requested).ShouldBeFalse();
        ShouldBeRestricted(() => restriction.EnsureAllows([requested]));
    }

    [Fact]
    public void Restricted_EnsureUnrestricted_Throws()
    {
        var restriction = MachineAnnotationRestriction.From("exe.northwind.invoicing.prod");

        var exception = ShouldBeRestricted(restriction.EnsureUnrestricted);

        exception.Message.ShouldContain("exe.northwind.invoicing.prod");
    }

    [Fact]
    public void Restricted_EnsureAllows_PassesWhenEveryKeyIsInside()
    {
        var restriction = MachineAnnotationRestriction.From("sbt.northwind");

        Should.NotThrow(() => restriction.EnsureAllows(
            ["sbt.northwind", "cnt.northwind.prod", "usg.northwind.invoicing", "uxe.northwind.invoicing.prod.export"]));
    }

    [Fact]
    public void Restricted_EnsureAllows_ThrowsWhenOneKeyIsOutside()
    {
        var restriction = MachineAnnotationRestriction.From("sbt.northwind");

        var exception = ShouldBeRestricted(() => restriction.EnsureAllows(
            ["cnt.northwind.prod", "cnt.contoso.prod", "usg.northwind.invoicing"]));

        exception.Message.ShouldContain("cnt.contoso.prod");
    }

    [Fact]
    public void Restricted_EnsureAllows_PassesOnAnEmptyList()
    {
        var restriction = MachineAnnotationRestriction.From("sbt.northwind");

        Should.NotThrow(() => restriction.EnsureAllows([]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_BlankKey_IsValidWithoutAKey(string? key)
    {
        MachineAnnotationRestriction.TryNormalize(key, out var normalized).ShouldBeTrue();
        normalized.ShouldBeNull();
    }

    [Theory]
    [InlineData("sbt.northwind", "sbt.northwind")]
    [InlineData("SBT..payments", "sbt.payments")]
    [InlineData("Exe.northwind.invoicing.prod", "exe.northwind.invoicing.prod")]
    [InlineData("RST.Invoicing", "rst.Invoicing")]
    [InlineData(".uxe.Northwind..Invoicing.Prod.Export.", "uxe.Northwind.Invoicing.Prod.Export")]
    [InlineData("unt.invoicing.export", "unt.invoicing.export")]
    public void TryNormalize_ValidKey_ReturnsTheCanonicalForm(string key, string expected)
    {
        MachineAnnotationRestriction.TryNormalize(key, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData("my-annotation-key")]
    [InlineData("sbt")]
    [InlineData("xyz.northwind")]
    [InlineData("sbt.northwind.prod")]
    [InlineData("uxe.northwind.invoicing.prod")]
    [InlineData("exe.a.b.c.d.e")]
    public void TryNormalize_InvalidKey_ReturnsFalse(string key)
    {
        MachineAnnotationRestriction.TryNormalize(key, out var normalized).ShouldBeFalse();
        normalized.ShouldBeNull();
    }

    private static AccessDeniedException ShouldBeRestricted(Action action)
    {
        var exception = Should.Throw<AccessDeniedException>(action);
        exception.ErrorCode.ShouldBe(ErrorCodes.AnnotationRestricted);
        return exception;
    }
}
