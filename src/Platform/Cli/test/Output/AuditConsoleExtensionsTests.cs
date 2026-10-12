using System;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

public class AuditConsoleExtensionsTests
{
    [Fact]
    public void Fields_ShowThePreviousAndTheNewValue()
    {
        var entry = Entry(AnnotationAuditEntryAction.Updated);
        entry.Fields =
        [
            new AnnotationFieldChange { Field = "Title", Previous = "Old", Value = "New" },
            new AnnotationFieldChange { Field = "IsDisabled", Previous = null, Value = true },
        ];

        AuditConsoleExtensions.FormatChanges(entry).ShouldBe("Title: \"Old\" → \"New\"; IsDisabled: null → true");
    }

    [Fact]
    public void Description_IsOnlyFlagged()
    {
        var entry = Entry(AnnotationAuditEntryAction.Updated);
        entry.Fields = [new AnnotationFieldChange { Field = "Description" }];

        AuditConsoleExtensions.FormatChanges(entry).ShouldBe("Description changed");
    }

    [Fact]
    public void TruncatedStrings_EndWithAnEllipsis()
    {
        var entry = Entry(AnnotationAuditEntryAction.Updated);
        entry.Fields = [new AnnotationFieldChange { Field = "Title", Previous = "Short", Value = "Very long", IsValueTruncated = true }];

        AuditConsoleExtensions.FormatChanges(entry).ShouldBe("Title: \"Short\" → \"Very long…\"");
    }

    [Fact]
    public void Labels_ListTheAddedAndTheRemoved()
    {
        var entry = Entry(AnnotationAuditEntryAction.Updated);
        entry.Labels = new AnnotationLabelChanges { Added = ["eu"], Removed = ["us"] };

        AuditConsoleExtensions.FormatChanges(entry).ShouldBe("+eu −us");
    }

    [Fact]
    public void Values_ShowPrimitivesAndOnlyTheKindOfObjects()
    {
        var entry = Entry(AnnotationAuditEntryAction.Updated);
        entry.Values =
        [
            Value("plan", AnnotationValueChangeChange.Changed, AnnotationValueChangePreviousKind.String, "pro", AnnotationValueChangeValueKind.String, "ent"),
            Value("limits", AnnotationValueChangeChange.Changed, AnnotationValueChangePreviousKind.Number, new JValue(5L), AnnotationValueChangeValueKind.Object, null),
            Value("tier", AnnotationValueChangeChange.Added, null, null, AnnotationValueChangeValueKind.Array, null),
            Value("legacy", AnnotationValueChangeChange.Removed, AnnotationValueChangePreviousKind.Boolean, true, null, null),
        ];

        AuditConsoleExtensions.FormatChanges(entry).ShouldBe("plan: \"pro\" → \"ent\"; limits: 5 → {…}; +tier: […]; −legacy");
    }

    [Fact]
    public void Cause_DescendantsAndConfigurationVersion_AreNamed()
    {
        var cascade = Entry(AnnotationAuditEntryAction.Deleted);
        cascade.Cause = new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Cascade, Key = "sbt.northwind" };

        var root = Entry(AnnotationAuditEntryAction.Deleted);
        root.DescendantCount = 4;

        var ancestor = Entry(AnnotationAuditEntryAction.Created);
        ancestor.Cause = new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = "exe.a.b.c" };

        var configuration = Entry(AnnotationAuditEntryAction.ConfigurationChanged);
        configuration.ConfigurationVersion = 7;

        AuditConsoleExtensions.FormatChanges(cascade).ShouldBe("with sbt.northwind");
        AuditConsoleExtensions.FormatChanges(root).ShouldBe("4 descendants");
        AuditConsoleExtensions.FormatChanges(ancestor).ShouldBe("ancestor of exe.a.b.c");
        AuditConsoleExtensions.FormatChanges(configuration).ShouldBe("config v7");
    }

    [Theory]
    [InlineData(AuditActorKind.Account, "user_1", "Ales Kobr", "Ales Kobr")]
    [InlineData(AuditActorKind.Account, "user_1", null, "user_1")]
    [InlineData(AuditActorKind.Machine, "11111111-2222", null, "machine:11111111-2222")]
    [InlineData(AuditActorKind.System, null, null, "system")]
    public void Actors_ArePrintedByNameOrId(AuditActorKind kind, string? id, string? name, string expected)
    {
        AuditConsoleExtensions.FormatActor(new AuditActor { Kind = kind, Id = id, Name = name }).ShouldBe(expected);
    }

    [Fact]
    public void Actions_HaveReadableNames()
    {
        AuditConsoleExtensions.FormatAction(AnnotationAuditEntryAction.Recreated).ShouldBe("Re-created");
        AuditConsoleExtensions.FormatAction(AnnotationAuditEntryAction.ConfigurationChanged).ShouldBe("Config changed");
        AuditConsoleExtensions.FormatAction(AnnotationAuditEntryAction.Restored).ShouldBe("Restored");
    }

    private static AnnotationAuditEntry Entry(AnnotationAuditEntryAction action)
    {
        return new AnnotationAuditEntry
        {
            Id = "e1",
            At = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero),
            Action = action,
            Actor = new AuditActor { Kind = AuditActorKind.Account, Id = "user_1", Name = "Ales Kobr" },
        };
    }

    private static AnnotationValueChange Value(
        string key,
        AnnotationValueChangeChange change,
        AnnotationValueChangePreviousKind? previousKind,
        object? previous,
        AnnotationValueChangeValueKind? valueKind,
        object? value)
    {
        return new AnnotationValueChange
        {
            Key = key,
            Change = change,
            PreviousKind = previousKind,
            Previous = previous,
            ValueKind = valueKind,
            Value = value,
        };
    }
}
