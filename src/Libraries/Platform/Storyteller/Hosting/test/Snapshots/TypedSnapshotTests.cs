using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Snapshots;

// The snapshot of each annotation type, as the mapper builds it from a point read: the type and the names from the key,
// the relation names and the strings of a unit from a body of the key's type.
public sealed class TypedSnapshotTests
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AResponsibility_HasTheUnitNamesOfTheBody()
    {
        var responsibility = Map("rst.invoicing", json => json["UnitNames"] = new JArray("monthly", "daily"))
            .ShouldBeOfType<StorytellerResponsibility>();

        responsibility.Name.ShouldBe("invoicing");
        responsibility.UnitNames.ShouldBe(["monthly", "daily"]);
    }

    [Fact]
    public void AUnit_HasTheNamesOfTheKey_AndTheTypeAndDefinitionOfTheBody()
    {
        var unit = Map("unt.invoicing.daily", json =>
            {
                json["ResponsibilityName"] = "other";
                json["ResponsibilityKey"] = "rst.other";
                json["UnitType"] = "job";
                json["UnitDefinition"] = "cron: 0 2 * * *";
            })
            .ShouldBeOfType<StorytellerUnit>();

        unit.ResponsibilityName.ShouldBe("invoicing");
        unit.UnitName.ShouldBe("daily");
        unit.Name.ShouldBe("daily");
        unit.UnitType.ShouldBe("job");
        unit.UnitDefinition.ShouldBe("cron: 0 2 * * *");
    }

    [Fact]
    public void ASubject_HasTheContextAndResponsibilityNamesOfTheBody()
    {
        var subject = Map("sbt.northwind", json =>
            {
                json["ContextNames"] = new JArray("prod", "dev");
                json["ResponsibilityNames"] = new JArray("invoicing");
            })
            .ShouldBeOfType<StorytellerSubject>();

        subject.ContextNames.ShouldBe(["prod", "dev"]);
        subject.ResponsibilityNames.ShouldBe(["invoicing"]);
    }

    [Fact]
    public void AUsage_HasTheNamesOfTheKey_AndTheContextNamesOfTheBody()
    {
        var usage = Map("usg.northwind.invoicing", json =>
            {
                json["SubjectName"] = "other";
                json["ResponsibilityName"] = "other";
                json["ContextNames"] = new JArray("prod");
            })
            .ShouldBeOfType<StorytellerUsage>();

        usage.SubjectName.ShouldBe("northwind");
        usage.ResponsibilityName.ShouldBe("invoicing");
        usage.ContextNames.ShouldBe(["prod"]);
    }

    [Fact]
    public void AContext_HasTheNamesOfTheKey_AndTheResponsibilityNamesOfTheBody()
    {
        var context = Map("cnt.northwind.prod", json =>
            {
                json["SubjectName"] = "other";
                json["ResponsibilityNames"] = new JArray("invoicing", "payroll");
            })
            .ShouldBeOfType<StorytellerContext>();

        context.SubjectName.ShouldBe("northwind");
        context.ContextName.ShouldBe("prod");
        context.ResponsibilityNames.ShouldBe(["invoicing", "payroll"]);
    }

    [Fact]
    public void AUnitOfExecution_HasTheNamesOfTheKey()
    {
        var unitOfExecution = Map("uxe.northwind.invoicing.prod.daily", json =>
            {
                json["SubjectName"] = "other";
                json["ResponsibilityName"] = "other";
                json["ContextName"] = "other";
                json["UnitName"] = "other";
            })
            .ShouldBeOfType<StorytellerUnitOfExecution>();

        unitOfExecution.SubjectName.ShouldBe("northwind");
        unitOfExecution.ResponsibilityName.ShouldBe("invoicing");
        unitOfExecution.ContextName.ShouldBe("prod");
        unitOfExecution.UnitName.ShouldBe("daily");
    }

    // The discriminator decides the SDK's type of the body: without it, or with an unknown, another or a differently
    // cased one, the body is not the key's type, and the snapshot is the key's type with empty lists and nulls.
    [Theory]
    [InlineData("rst.invoicing", null)]
    [InlineData("rst.invoicing", "Subject")]
    [InlineData("rst.invoicing", "responsibility")]
    [InlineData("unt.invoicing.daily", "Nonsense")]
    [InlineData("sbt.northwind", "Responsibility")]
    [InlineData("usg.northwind.invoicing", null)]
    [InlineData("cnt.northwind.prod", "Usage")]
    [InlineData("exe.northwind.invoicing.prod", "Context")]
    [InlineData("uxe.northwind.invoicing.prod.daily", null)]
    public void ABodyOfAnotherType_GivesTheTypeOfTheKey_WithoutRelations(string annotationKey, string? discriminator)
    {
        var snapshot = Map(annotationKey, json =>
        {
            json.Remove("AnnotationType");

            if (discriminator is not null)
            {
                json["AnnotationType"] = discriminator;
            }

            json["UnitNames"] = new JArray("daily");
            json["ContextNames"] = new JArray("prod");
            json["ResponsibilityNames"] = new JArray("invoicing");
            json["UnitType"] = "job";
            json["UnitDefinition"] = "cron";
        });

        snapshot.GetType().ShouldBe(SnapshotTypes.Of(AnnotationKey.Parse(annotationKey).Type));
        Relations(snapshot).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("rst.invoicing")]
    [InlineData("unt.invoicing.daily")]
    [InlineData("sbt.northwind")]
    [InlineData("usg.northwind.invoicing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData("exe.northwind.invoicing.prod")]
    [InlineData("uxe.northwind.invoicing.prod.daily")]
    public void ABodyWithoutRelations_HasEmptyLists(string annotationKey)
    {
        var snapshot = Map(annotationKey);

        snapshot.GetType().ShouldBe(SnapshotTypes.Of(AnnotationKey.Parse(annotationKey).Type));
        Relations(snapshot).ShouldBeEmpty();
    }

    [Fact]
    public void SnapshotTypes_MapEveryTypeBothWays()
    {
        foreach (var type in Enum.GetValues<AnnotationType>())
        {
            SnapshotTypes.TypeOf(SnapshotTypes.Of(type)).ShouldBe(type);
        }

        SnapshotTypes.TypeOf(typeof(StorytellerAnnotation)).ShouldBeNull();
        Should.Throw<ArgumentException>(() => SnapshotTypes.Require<StorytellerAnnotation>("TAnnotation")).ParamName.ShouldBe("TAnnotation");
    }

    // Who am I turns a key that is gone into a deleted copy of its snapshot.
    [Theory]
    [InlineData("rst.invoicing")]
    [InlineData("unt.invoicing.daily")]
    [InlineData("sbt.northwind")]
    [InlineData("usg.northwind.invoicing")]
    [InlineData("cnt.northwind.prod")]
    [InlineData("exe.northwind.invoicing.prod")]
    [InlineData("uxe.northwind.invoicing.prod.daily")]
    public void ADeletedCopy_KeepsTheTypeTheNamesAndTheRelations(string annotationKey)
    {
        var snapshot = Map(annotationKey, json =>
        {
            json["Title"] = "Title";
            json["UnitNames"] = new JArray("daily");
            json["ContextNames"] = new JArray("prod");
            json["ResponsibilityNames"] = new JArray("invoicing");
            json["UnitType"] = "job";
            json["UnitDefinition"] = "cron";
        });

        var deleted = snapshot.WithDeletedAt(DeletedAt);

        deleted.ShouldNotBeSameAs(snapshot);
        deleted.GetType().ShouldBe(snapshot.GetType());
        deleted.DeletedAt.ShouldBe(DeletedAt);
        deleted.Key.ShouldBe(snapshot.Key);
        deleted.Title.ShouldBe("Title");
        Relations(deleted).ShouldBe(Relations(snapshot));
        Names(deleted).ShouldBe(Names(snapshot));
        snapshot.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void ADeletedCopyOfAResponsibility_KeepsItsUnitNames()
    {
        var deleted = Map("rst.invoicing", json => json["UnitNames"] = new JArray("daily")).WithDeletedAt(DeletedAt);

        deleted.ShouldBeOfType<StorytellerResponsibility>().UnitNames.ShouldBe(["daily"]);
    }

    // Applications build them for test doubles: what an initializer sets is copied, and null gives an empty list.
    [Fact]
    public void SnapshotsThatAnApplicationBuilt_CopyTheirLists()
    {
        var names = new List<string> { "daily" };
        var responsibility = new StorytellerResponsibility { Key = "@acme.billing.default.rst.invoicing", AnnotationKey = "rst.invoicing", Type = AnnotationType.Responsibility, Name = "invoicing", UnitNames = names };
        var subject = new StorytellerSubject { Key = "@acme.billing.default.sbt.northwind", AnnotationKey = "sbt.northwind", Type = AnnotationType.Subject, Name = "northwind", ContextNames = names, ResponsibilityNames = names };
        var usage = new StorytellerUsage { Key = "@acme.billing.default.usg.northwind.invoicing", AnnotationKey = "usg.northwind.invoicing", Type = AnnotationType.Usage, Name = "invoicing", SubjectName = "northwind", ResponsibilityName = "invoicing", ContextNames = names };
        var context = new StorytellerContext { Key = "@acme.billing.default.cnt.northwind.prod", AnnotationKey = "cnt.northwind.prod", Type = AnnotationType.Context, Name = "prod", SubjectName = "northwind", ContextName = "prod", ResponsibilityNames = names };

        names.Add("changed");

        responsibility.UnitNames.ShouldBe(["daily"]);
        subject.ContextNames.ShouldBe(["daily"]);
        subject.ResponsibilityNames.ShouldBe(["daily"]);
        usage.ContextNames.ShouldBe(["daily"]);
        context.ResponsibilityNames.ShouldBe(["daily"]);
        Should.Throw<NotSupportedException>(() => ((IList<string>)responsibility.UnitNames).Add("changed"));
    }

    [Fact]
    public void SnapshotsThatAnApplicationBuiltWithNull_HaveEmptyLists()
    {
        var responsibility = new StorytellerResponsibility { Key = "@acme.billing.default.rst.invoicing", AnnotationKey = "rst.invoicing", Type = AnnotationType.Responsibility, Name = "invoicing", UnitNames = null! };
        var subject = new StorytellerSubject { Key = "@acme.billing.default.sbt.northwind", AnnotationKey = "sbt.northwind", Type = AnnotationType.Subject, Name = "northwind", ContextNames = null!, ResponsibilityNames = null! };
        var usage = new StorytellerUsage { Key = "@acme.billing.default.usg.northwind.invoicing", AnnotationKey = "usg.northwind.invoicing", Type = AnnotationType.Usage, Name = "invoicing", SubjectName = "northwind", ResponsibilityName = "invoicing", ContextNames = null! };
        var context = new StorytellerContext { Key = "@acme.billing.default.cnt.northwind.prod", AnnotationKey = "cnt.northwind.prod", Type = AnnotationType.Context, Name = "prod", SubjectName = "northwind", ContextName = "prod", ResponsibilityNames = null! };
        var unit = new StorytellerUnit { Key = "@acme.billing.default.unt.invoicing.daily", AnnotationKey = "unt.invoicing.daily", Type = AnnotationType.Unit, Name = "daily", ResponsibilityName = "invoicing", UnitName = "daily" };

        responsibility.UnitNames.ShouldBeEmpty();
        subject.ContextNames.ShouldBeEmpty();
        subject.ResponsibilityNames.ShouldBeEmpty();
        usage.ContextNames.ShouldBeEmpty();
        context.ResponsibilityNames.ShouldBeEmpty();
        unit.UnitType.ShouldBeNull();
        unit.UnitDefinition.ShouldBeNull();
    }

    // Applications build snapshots for test doubles; the test project sees the internals, so the accessors are checked by
    // reflection.
    [Theory]
    [InlineData(typeof(StorytellerResponsibility), nameof(StorytellerResponsibility.UnitNames))]
    [InlineData(typeof(StorytellerUnit), nameof(StorytellerUnit.ResponsibilityName))]
    [InlineData(typeof(StorytellerUnit), nameof(StorytellerUnit.UnitName))]
    [InlineData(typeof(StorytellerUnit), nameof(StorytellerUnit.UnitType))]
    [InlineData(typeof(StorytellerUnit), nameof(StorytellerUnit.UnitDefinition))]
    [InlineData(typeof(StorytellerSubject), nameof(StorytellerSubject.ContextNames))]
    [InlineData(typeof(StorytellerSubject), nameof(StorytellerSubject.ResponsibilityNames))]
    [InlineData(typeof(StorytellerUsage), nameof(StorytellerUsage.SubjectName))]
    [InlineData(typeof(StorytellerUsage), nameof(StorytellerUsage.ResponsibilityName))]
    [InlineData(typeof(StorytellerUsage), nameof(StorytellerUsage.ContextNames))]
    [InlineData(typeof(StorytellerContext), nameof(StorytellerContext.SubjectName))]
    [InlineData(typeof(StorytellerContext), nameof(StorytellerContext.ContextName))]
    [InlineData(typeof(StorytellerContext), nameof(StorytellerContext.ResponsibilityNames))]
    [InlineData(typeof(StorytellerUnitOfExecution), nameof(StorytellerUnitOfExecution.SubjectName))]
    [InlineData(typeof(StorytellerUnitOfExecution), nameof(StorytellerUnitOfExecution.ResponsibilityName))]
    [InlineData(typeof(StorytellerUnitOfExecution), nameof(StorytellerUnitOfExecution.ContextName))]
    [InlineData(typeof(StorytellerUnitOfExecution), nameof(StorytellerUnitOfExecution.UnitName))]
    public void TheMembersOfATypedSnapshot_AreInitializableByApplications(Type type, string property)
    {
        type.IsSealed.ShouldBeTrue();
        type.BaseType.ShouldBe(typeof(StorytellerAnnotation));
        type.GetConstructor(Type.EmptyTypes).ShouldNotBeNull().IsPublic.ShouldBeTrue();
        type.GetProperty(property).ShouldNotBeNull().SetMethod.ShouldNotBeNull().IsPublic.ShouldBeTrue();
    }

    private static StorytellerAnnotation Map(string annotationKey, Action<JObject>? configure = null)
    {
        var annotation = JObject.Parse(TestAnnotations.Json(annotationKey, configure)).ToObject<Annotation>()!;
        return AnnotationMapper.Map(FullKey.Parse($"{HostingFixture.Scope}.{annotationKey}"), annotation);
    }

    // Every relation name and string a typed snapshot carries from the body.
    private static IReadOnlyList<string> Relations(StorytellerAnnotation snapshot)
    {
        return snapshot switch
        {
            StorytellerResponsibility responsibility => [.. responsibility.UnitNames],
            StorytellerUnit unit => new[] { unit.UnitType, unit.UnitDefinition }.OfType<string>().ToList(),
            StorytellerSubject subject => [.. subject.ContextNames, .. subject.ResponsibilityNames],
            StorytellerUsage usage => [.. usage.ContextNames],
            StorytellerContext context => [.. context.ResponsibilityNames],
            StorytellerExecution execution => [.. execution.UnitNames],
            _ => [],
        };
    }

    // The names a typed snapshot takes from its key.
    private static IReadOnlyList<string> Names(StorytellerAnnotation snapshot)
    {
        return snapshot switch
        {
            StorytellerUnit unit => [unit.ResponsibilityName, unit.UnitName],
            StorytellerUsage usage => [usage.SubjectName, usage.ResponsibilityName],
            StorytellerContext context => [context.SubjectName, context.ContextName],
            StorytellerExecution execution => [execution.SubjectName, execution.ResponsibilityName, execution.ContextName],
            StorytellerUnitOfExecution unitOfExecution => [unitOfExecution.SubjectName, unitOfExecution.ResponsibilityName, unitOfExecution.ContextName, unitOfExecution.UnitName],
            _ => [snapshot.Name],
        };
    }
}
