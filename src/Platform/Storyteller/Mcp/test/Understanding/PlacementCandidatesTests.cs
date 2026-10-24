using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.Understanding;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class PlacementCandidatesTests
{
    [Theory]
    [InlineData("northwind", "invoicing", "prod", "export", "uxe.northwind.invoicing.prod.export")]
    [InlineData("northwind", "invoicing", "prod", null, "exe.northwind.invoicing.prod")]
    [InlineData("northwind", "invoicing", null, null, "usg.northwind.invoicing")]
    [InlineData("northwind", null, "prod", null, "cnt.northwind.prod")]
    [InlineData(null, "invoicing", null, "export", "unt.invoicing.export")]
    [InlineData("northwind", null, null, null, "sbt.northwind")]
    [InlineData(null, "invoicing", null, null, "rst.invoicing")]
    public void KeyOf_GivesTheMostSpecificKey(string? subject, string? responsibility, string? context, string? unit, string expected)
    {
        var system = new PlacementSystem { Subject = subject, Responsibility = responsibility, Context = context, Unit = unit };

        PlacementAdvisor.KeyOf(system, 0).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, null, null, null, "neither a subject nor a responsibility")]
    [InlineData("northwind", null, null, "export", "systems[0].unit' needs the responsibility")]
    [InlineData(null, "invoicing", "prod", null, "systems[0].context' needs the subject")]
    [InlineData("northwind", "invoicing", null, "export", "needs the context too")]
    [InlineData("north wind", null, null, null, "'systems[0].subject' can contain only letters")]
    public void KeyOf_OtherCombinations_AreInputErrors(string? subject, string? responsibility, string? context, string? unit, string message)
    {
        var system = new PlacementSystem { Subject = subject, Responsibility = responsibility, Context = context, Unit = unit };

        Should.Throw<ToolInputException>(() => PlacementAdvisor.KeyOf(system, 0)).Message.ShouldContain(message);
    }

    [Fact]
    public void KeyOf_ALongName_IsAnInputError()
    {
        var system = new PlacementSystem { Subject = new string('a', 51) };

        Should.Throw<ToolInputException>(() => PlacementAdvisor.KeyOf(system, 2)).Message.ShouldContain("'systems[2].subject' is too long");
    }

    [Fact]
    public void OneSystem_ItsKeyThenItsAncestors_DeepestFirst()
    {
        var (candidates, notice) = PlacementCandidates.For([AnnotationKey.Parse("exe.northwind.invoicing.prod")], typeDefault: false);

        // equal depth in reverse merge order: the context is merged after the usage, the subject after the responsibility
        candidates.Select(candidate => candidate.AnnotationKey).ShouldBe(
            ["exe.northwind.invoicing.prod", "cnt.northwind.prod", "usg.northwind.invoicing", "sbt.northwind", "rst.invoicing"]);
        candidates[0].Reason.ShouldBe("The value belongs to this system only.");
        candidates[0].Meaning.ShouldBe(PlacementCandidates.MeaningOf(AnnotationType.Execution));
        candidates.ShouldAllBe(candidate => candidate.Kind == PlacementCandidate.AnnotationKind);
        notice.ShouldBeNull();
    }

    [Fact]
    public void OneUnitOfExecution_ListsTheUnitWithItsMeaning()
    {
        var (candidates, _) = PlacementCandidates.For([AnnotationKey.Parse("uxe.northwind.invoicing.prod.export")], typeDefault: false);

        candidates.Select(candidate => candidate.AnnotationKey).ShouldBe(
        [
            "uxe.northwind.invoicing.prod.export", "exe.northwind.invoicing.prod", "cnt.northwind.prod", "usg.northwind.invoicing",
            "unt.invoicing.export", "sbt.northwind", "rst.invoicing",
        ]);
        candidates.Single(candidate => candidate.AnnotationKey == "unt.invoicing.export").Meaning.ShouldBe("This job of the capability, for every subject (the unit).");
    }

    [Fact]
    public void SeveralSystems_TheirLowestCommonAncestorFirst()
    {
        var (candidates, notice) = PlacementCandidates.For(
            [AnnotationKey.Parse("exe.northwind.invoicing.prod"), AnnotationKey.Parse("exe.northwind.invoicing.test")],
            typeDefault: false);

        candidates.Select(candidate => candidate.AnnotationKey).ShouldBe(["usg.northwind.invoicing", "sbt.northwind", "rst.invoicing"]);
        candidates[0].Reason.ShouldContain("shared by these systems");
        notice.ShouldBeNull();
    }

    [Fact]
    public void SeveralSystems_CanShareTwoLowestAncestors()
    {
        var (candidates, _) = PlacementCandidates.For(
            [AnnotationKey.Parse("uxe.northwind.invoicing.prod.export"), AnnotationKey.Parse("uxe.northwind.invoicing.test.export")],
            typeDefault: false);

        candidates.Select(candidate => candidate.AnnotationKey).ShouldBe(["usg.northwind.invoicing", "unt.invoicing.export", "sbt.northwind", "rst.invoicing"]);
        candidates.Take(2).ShouldAllBe(candidate => candidate.Reason.Contains("the most specific annotation they all share"));
    }

    [Fact]
    public void SeveralSystems_WithoutACommonAncestor_SaySo()
    {
        var (candidates, notice) = PlacementCandidates.For([AnnotationKey.Parse("sbt.northwind"), AnnotationKey.Parse("sbt.contoso")], typeDefault: false);

        candidates.ShouldBeEmpty();
        notice.ShouldNotBeNull().ShouldContain("share no annotation");
        notice.ShouldContain("typeDefault");
    }

    [Fact]
    public void TypeDefault_TheTemplateComesFirst()
    {
        var (candidates, notice) = PlacementCandidates.For([AnnotationKey.Parse("sbt.northwind"), AnnotationKey.Parse("sbt.contoso")], typeDefault: true);

        var template = candidates.ShouldHaveSingleItem();
        template.Kind.ShouldBe(PlacementCandidate.TemplateKind);
        template.TemplateTypeCode.ShouldBe("sbt");
        template.Level.ShouldBe("Subject");
        template.Meaning.ShouldBe(PlacementCandidates.TemplateMeaning);
        notice.ShouldNotBeNull().ShouldContain("template above");
    }

    [Fact]
    public void TypeDefault_SystemsOfSeveralTypes_IsAnInputError()
    {
        Should.Throw<ToolInputException>(() => PlacementCandidates.For([AnnotationKey.Parse("sbt.northwind"), AnnotationKey.Parse("usg.northwind.invoicing")], typeDefault: true))
            .Message.ShouldContain("one annotation type");
    }

    [Fact]
    public void TheSameSystemTwice_IsOneSystem()
    {
        var (candidates, _) = PlacementCandidates.For([AnnotationKey.Parse("sbt.northwind"), AnnotationKey.Parse("sbt.northwind")], typeDefault: false);

        candidates.ShouldHaveSingleItem().Reason.ShouldBe("The value belongs to this system only.");
    }
}
