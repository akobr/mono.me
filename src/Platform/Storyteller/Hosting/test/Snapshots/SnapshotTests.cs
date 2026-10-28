using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Snapshots;

public sealed class SnapshotTests : IDisposable
{
    private const string Secretish = "renewal-at-the-end-of-the-month";

    private readonly HostingFixture _fixture = new();

    [Fact]
    public async Task TheTypeAndTheNames_ComeFromTheKey_AndTheRestFromTheBody()
    {
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json =>
        {
            json["AnnotationKey"] = "exe.other.other.other";
            json["Name"] = "other";
            json["SubjectName"] = "other";
            json["ResponsibilityName"] = "other";
            json["ContextName"] = "other";
            json["UnitNames"] = new JArray("daily", "monthly");
            json["Title"] = "Northwind invoicing";
            json["Description"] = "Production.";
            json["Labels"] = new JArray("eu", "gold");
            json["IsDisabled"] = true;
            json["ValidFrom"] = "2026-10-01T00:00:00+02:00";
            json["ExpiresAt"] = "2027-10-01T00:00:00+02:00";
            json["Created"] = Stamp("2026-09-01T08:00:00Z", "user_0");
            json["Modified"] = Stamp("2026-10-19T08:30:00Z", "user_1");
        });

        // The type code as the caller wrote it; the snapshot and the request lower-case it.
        var execution = (await Catalog().GetExecutionAsync($"{HostingFixture.Scope}.EXE.northwind.invoicing.prod")).ShouldNotBeNull();

        _fixture.Http.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe(HostingFixture.AnnotationPath(HostingFixture.ExecutionKey));
        execution.Key.ShouldBe($"{HostingFixture.Scope}.{HostingFixture.ExecutionKey}");
        execution.AnnotationKey.ShouldBe(HostingFixture.ExecutionKey);
        execution.Type.ShouldBe(AnnotationType.Execution);
        execution.Name.ShouldBe("prod");
        execution.SubjectName.ShouldBe("northwind");
        execution.ResponsibilityName.ShouldBe("invoicing");
        execution.ContextName.ShouldBe("prod");
        execution.UnitNames.ShouldBe(["daily", "monthly"]);
        execution.Title.ShouldBe("Northwind invoicing");
        execution.Description.ShouldBe("Production.");
        execution.Labels.ShouldBe(["eu", "gold"]);
        execution.IsDisabled.ShouldBeTrue();
        execution.ValidFrom.ShouldBe(DateTimeOffset.Parse("2026-10-01T00:00:00+02:00"));
        execution.ExpiresAt.ShouldBe(DateTimeOffset.Parse("2027-10-01T00:00:00+02:00"));
        execution.Created.ShouldNotBeNull().By.Id.ShouldBe("user_0");
        execution.Modified.ShouldNotBeNull().At.ShouldBe(new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero));
        execution.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task AnAnnotationWithoutTheOptionalMembers_HasEmptyCollections()
    {
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey);

        var execution = (await Catalog().GetExecutionAsync(HostingFixture.ExecutionKey)).ShouldNotBeNull();

        execution.Labels.ShouldBeEmpty();
        execution.Values.ShouldBeEmpty();
        execution.UnitNames.ShouldBeEmpty();
        execution.Created.ShouldBeNull();
        execution.Modified.ShouldBeNull();
        execution.Title.ShouldBeNull();
        execution.IsDisabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Values_AreADeepCopyPerAccess_AndDateLikeStringsStayStrings()
    {
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json => json["Values"] = new JObject
        {
            ["tier"] = "gold",
            ["seats"] = 25,
            ["renewal"] = "2026-10-20T12:00:00.1230000+02:00",
            ["plan"] = new JObject { ["name"] = "pro", ["features"] = new JArray("sso", "audit") },
        });
        var execution = (await Catalog().GetExecutionAsync(HostingFixture.ExecutionKey)).ShouldNotBeNull();

        var values = execution.Values;
        var plan = values["plan"].ShouldBeOfType<JObject>();
        plan["name"] = "changed";
        ((JArray)plan["features"]!).Add("changed");

        var again = execution.Values;
        again.ShouldNotBeSameAs(values);
        again["plan"].ShouldBeOfType<JObject>()["name"]!.Value<string>().ShouldBe("pro");
        again["plan"].ShouldBeOfType<JObject>()["features"]!.Values<string>().ShouldBe(["sso", "audit"]);
        again["tier"].ShouldBe("gold");
        again["seats"].ShouldBe(25L);
        again["renewal"].ShouldBeOfType<string>().ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        Should.Throw<NotSupportedException>(() => ((IDictionary<string, object?>)again)["tier"] = "silver");
    }

    [Fact]
    public async Task AuditStamps_AreACopyPerAccess()
    {
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json =>
        {
            json["Created"] = Stamp("2026-09-01T08:00:00+02:00", "user_0");
            json["Modified"] = Stamp("2026-10-19T08:30:00+02:00", "user_1");
        });
        var execution = (await Catalog().GetExecutionAsync(HostingFixture.ExecutionKey)).ShouldNotBeNull();

        var modified = execution.Modified.ShouldNotBeNull();
        modified.At = DateTimeOffset.MinValue;
        modified.By.Id = "changed";
        execution.Created.ShouldNotBeNull().By.Id = "changed";

        execution.Modified.ShouldNotBeSameAs(modified);
        execution.Modified!.At.ShouldBe(DateTimeOffset.Parse("2026-10-19T08:30:00+02:00"));
        execution.Modified.At.Offset.ShouldBe(TimeSpan.FromHours(2));
        execution.Modified.By.Id.ShouldBe("user_1");
        execution.Created!.By.Id.ShouldBe("user_0");
    }

    [Fact]
    public async Task ToString_NamesTheKeyAndTheType_ButNoValues()
    {
        _fixture.RouteAnnotation(HostingFixture.ExecutionKey, json =>
        {
            json["Title"] = "Northwind";
            json["Values"] = new JObject { ["note"] = Secretish };
        });
        var execution = (await Catalog().GetExecutionAsync(HostingFixture.ExecutionKey)).ShouldNotBeNull();

        var text = execution.ToString();

        text.ShouldBe($"{HostingFixture.Scope}.{HostingFixture.ExecutionKey} (Execution)");
        text.ShouldNotContain(Secretish);
    }

    [Theory]
    [InlineData("2026-10-27T07:59:59Z", AnnotationStatus.Scheduled)]
    [InlineData("2026-10-27T08:00:00Z", AnnotationStatus.Active)]
    [InlineData("2026-10-28T07:59:59Z", AnnotationStatus.Active)]
    [InlineData("2026-10-28T08:00:00Z", AnnotationStatus.Expired)]
    public void TheStatus_IsActiveFromValidFromUntilExpiresAt(string now, AnnotationStatus status)
    {
        var annotation = new StorytellerAnnotation
        {
            Key = $"{HostingFixture.Scope}.usg.northwind.invoicing",
            AnnotationKey = "usg.northwind.invoicing",
            Type = AnnotationType.Usage,
            Name = "invoicing",
            ValidFrom = DateTimeOffset.Parse("2026-10-27T10:00:00+02:00"),
            ExpiresAt = DateTimeOffset.Parse("2026-10-28T08:00:00Z"),
        };

        annotation.GetStatus(DateTimeOffset.Parse(now)).ShouldBe(status);
        annotation.Labels.ShouldBeEmpty();
        annotation.Values.ShouldBeEmpty();
    }

    [Fact]
    public void Disabled_WinsOverTheValidityWindow()
    {
        var annotation = new StorytellerAnnotation
        {
            Key = $"{HostingFixture.Scope}.usg.northwind.invoicing",
            AnnotationKey = "usg.northwind.invoicing",
            Type = AnnotationType.Usage,
            Name = "invoicing",
            IsDisabled = true,
            ExpiresAt = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        };

        annotation.GetStatus(DateTimeOffset.Parse("2026-10-27T08:00:00Z")).ShouldBe(AnnotationStatus.Disabled);
    }

    // Applications build snapshots for test doubles of the catalog and of who am I; the test project sees the internals,
    // so the accessors are checked by reflection.
    [Theory]
    [InlineData(typeof(StorytellerAnnotation), nameof(StorytellerAnnotation.Labels))]
    [InlineData(typeof(StorytellerAnnotation), nameof(StorytellerAnnotation.Values))]
    [InlineData(typeof(StorytellerAnnotation), nameof(StorytellerAnnotation.Created))]
    [InlineData(typeof(StorytellerAnnotation), nameof(StorytellerAnnotation.Modified))]
    [InlineData(typeof(StorytellerAnnotation), nameof(StorytellerAnnotation.DeletedAt))]
    [InlineData(typeof(StorytellerExecution), nameof(StorytellerExecution.UnitNames))]
    [InlineData(typeof(StorytellerExecutionContext), nameof(StorytellerExecutionContext.Ancestors))]
    [InlineData(typeof(StorytellerExecutionContext), nameof(StorytellerExecutionContext.Unreadable))]
    public void TheMembersOfASnapshot_AreInitializableByApplications(Type type, string property)
    {
        type.GetConstructor(Type.EmptyTypes).ShouldNotBeNull().IsPublic.ShouldBeTrue();
        type.GetProperty(property).ShouldNotBeNull().SetMethod.ShouldNotBeNull().IsPublic.ShouldBeTrue();
    }

    // Only the subscriptions build one, as only they hold documents.
    [Fact]
    public void ASubscription_IsNotConstructibleByApplications_AndHasNoSetters()
    {
        typeof(StorytellerSubscription).GetConstructors().ShouldBeEmpty();
        typeof(StorytellerSubscription).GetProperties().ShouldAllBe(property => property.SetMethod == null);
    }

    [Fact]
    public void ASnapshotThatAnApplicationBuilt_CopiesWhatItWasGiven()
    {
        var labels = new List<string> { "gold" };
        var plan = new JObject { ["name"] = "pro" };
        var values = new Dictionary<string, object?> { ["plan"] = plan, ["seats"] = 25 };
        var created = new AuditStamp { At = DateTimeOffset.Parse("2026-09-01T08:00:00+02:00"), By = new AuditActor { Kind = AuditActorKind.Account, Id = "user_0" } };
        var units = new List<string> { "daily" };
        var usage = new StorytellerAnnotation
        {
            Key = $"{HostingFixture.Scope}.usg.northwind.invoicing",
            AnnotationKey = "usg.northwind.invoicing",
            Type = AnnotationType.Usage,
            Name = "invoicing",
            Labels = labels,
            Values = values,
            Created = created,
        };
        var execution = new StorytellerExecution
        {
            Key = $"{HostingFixture.Scope}.{HostingFixture.ExecutionKey}",
            AnnotationKey = HostingFixture.ExecutionKey,
            Type = AnnotationType.Execution,
            Name = "prod",
            SubjectName = "northwind",
            ResponsibilityName = "invoicing",
            ContextName = "prod",
            UnitNames = units,
        };
        var ancestors = new Dictionary<AnnotationType, StorytellerAnnotation> { [AnnotationType.Usage] = usage };
        var unreadable = new List<string> { $"{HostingFixture.Scope}.sbt.northwind" };
        var context = new StorytellerExecutionContext
        {
            Key = execution.Key,
            Annotation = execution,
            Ancestors = ancestors,
            Unreadable = unreadable,
            ReadAt = DateTimeOffset.Parse("2026-10-28T08:00:00Z"),
        };

        labels.Add("changed");
        plan["name"] = "changed";
        values["seats"] = 1;
        created.By.Id = "changed";
        units.Add("changed");
        ancestors.Clear();
        unreadable.Clear();

        usage.Labels.ShouldBe(["gold"]);
        usage.Values["plan"].ShouldBeOfType<JObject>()["name"]!.Value<string>().ShouldBe("pro");
        usage.Values["seats"].ShouldBe(25);
        usage.Created.ShouldNotBeNull().By.Id.ShouldBe("user_0");
        execution.UnitNames.ShouldBe(["daily"]);
        context.Usage.ShouldBeSameAs(usage);
        context.Unreadable.ShouldBe([$"{HostingFixture.Scope}.sbt.northwind"]);
        context.GetSubscriptionStatus(context.ReadAt).ShouldBe(StorytellerSubscriptionStatus.Active);
        Should.Throw<NotSupportedException>(() => ((IList<string>)usage.Labels).Add("changed"));
    }

    [Fact]
    public void ASnapshotInitializedWithNull_HasEmptyCollections()
    {
        var execution = new StorytellerExecution
        {
            Key = $"{HostingFixture.Scope}.{HostingFixture.ExecutionKey}",
            AnnotationKey = HostingFixture.ExecutionKey,
            Type = AnnotationType.Execution,
            Name = "prod",
            SubjectName = "northwind",
            ResponsibilityName = "invoicing",
            ContextName = "prod",
            Labels = null!,
            Values = null!,
            UnitNames = null!,
        };
        var context = new StorytellerExecutionContext
        {
            Key = execution.Key,
            Annotation = execution,
            Ancestors = null!,
            Unreadable = null!,
            ReadAt = DateTimeOffset.Parse("2026-10-28T08:00:00Z"),
        };

        execution.Labels.ShouldBeEmpty();
        execution.Values.ShouldBeEmpty();
        execution.UnitNames.ShouldBeEmpty();
        context.Ancestors.ShouldBeEmpty();
        context.Unreadable.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static JObject Stamp(string at, string id)
    {
        return new JObject { ["At"] = at, ["By"] = new JObject { ["Kind"] = "Account", ["Id"] = id } };
    }

    private ISubscriptionCatalog Catalog()
    {
        return _fixture.BuildServices().GetRequiredService<ISubscriptionCatalog>();
    }
}
