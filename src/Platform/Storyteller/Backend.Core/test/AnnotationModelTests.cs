using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class AnnotationModelTests
{
    [Fact]
    public void GetAllAncestorKeys_OfUnitOfExecution_ListsEveryAncestorOnceRootsFirst()
    {
        var key = AnnotationKey.CreateUnitOfExecution("northwind", "invoicing", "prod", "export");

        var ancestors = key.GetAllAncestorKeys().Select(ancestor => ancestor.ToString()).ToList();

        ancestors.ShouldBe(
        [
            "rst.invoicing",
            "sbt.northwind",
            "unt.invoicing.export",
            "usg.northwind.invoicing",
            "cnt.northwind.prod",
            "exe.northwind.invoicing.prod",
        ]);
    }

    [Theory]
    [InlineData("rst.invoicing", "usg.northwind.invoicing", true)]
    [InlineData("sbt.northwind", "exe.northwind.invoicing.prod", true)]
    [InlineData("cnt.northwind.prod", "uxe.northwind.invoicing.prod.export", true)]
    [InlineData("unt.invoicing.export", "uxe.northwind.invoicing.prod.export", true)]
    [InlineData("cnt.northwind.prod", "usg.northwind.invoicing", false)]
    [InlineData("sbt.northwind", "sbt.northwind", false)]
    [InlineData("sbt.contoso", "usg.northwind.invoicing", false)]
    public void IsDescendantOf_FollowsTheInheritanceGraph(string ancestor, string key, bool expected)
    {
        AnnotationKey.Parse(key).IsDescendantOf(AnnotationKey.Parse(ancestor)).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_TakesKeyMembersFromTheKey()
    {
        var input = new Annotation
        {
            ProjectName = "billing",
            ViewName = "default",
            AnnotationKey = "exe.northwind.invoicing.prod",
            Name = "wrong",
            AnnotationType = AnnotationType.Subject,
            Title = "Production invoicing",
        };

        var normalized = AnnotationFactory.Normalize(input, AnnotationKey.Parse(input.AnnotationKey));

        var execution = normalized.ShouldBeOfType<Execution>();
        execution.Name.ShouldBe("prod");
        execution.AnnotationType.ShouldBe(AnnotationType.Execution);
        execution.UsageKey.ShouldBe("usg.northwind.invoicing");
        execution.ContextKey.ShouldBe("cnt.northwind.prod");
        execution.Title.ShouldBe("Production invoicing");
        execution.UnitNames.ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_KeepsTheUnitTypeAndDefinition()
    {
        var key = AnnotationKey.CreateUnit("invoicing", "export");
        var input = (Unit)AnnotationFactory.CreateDefault(key, "billing", "default") with { UnitType = "timer", UnitDefinition = "0 2 * * *" };

        var unit = AnnotationFactory.Normalize(input, key).ShouldBeOfType<Unit>();

        unit.UnitType.ShouldBe("timer");
        unit.UnitDefinition.ShouldBe("0 2 * * *");
    }

    [Fact]
    public void CreateDefault_CopiesTheMembersOfAnAutomaticAncestor()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var descendant = AnnotationFactory.CreateDefault(AnnotationKey.CreateUsage("northwind", "invoicing"), "billing", "default") with
        {
            ValidFrom = from,
            TimeZone = "UTC",
            IsDisabled = true,
            Labels = ["eu"],
            Title = "not copied",
        };

        var subject = AnnotationFactory.CreateDefault(AnnotationKey.CreateSubject("northwind"), "billing", "default", descendant);

        subject.ValidFrom.ShouldBe(from);
        subject.TimeZone.ShouldBe("UTC");
        subject.IsDisabled.ShouldBe(true);
        subject.Labels.ShouldBe(["eu"]);
        subject.Title.ShouldBeNull();
    }

    [Fact]
    public void GetRemainingSeconds_CountsFromTheDeletion()
    {
        var deletedAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        DeletionRetention.GetRemainingSeconds(deletedAt, 90, deletedAt).ShouldBe(90 * 24 * 3600);
        DeletionRetention.GetRemainingSeconds(deletedAt, 90, deletedAt.AddDays(30)).ShouldBe(60 * 24 * 3600);
        DeletionRetention.GetRemainingSeconds(deletedAt, 90, deletedAt.AddDays(91)).ShouldBe(1);
    }

    [Fact]
    public void GetPurgeAt_IsTheLastWritePlusTheTtl()
    {
        DeletionRetention.GetPurgeAt(1_000, 60).ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1_060));
        DeletionRetention.GetPurgeAt(1_000, null).ShouldBeNull();
        DeletionRetention.GetPurgeAt(0, 60).ShouldBeNull();
    }
}
