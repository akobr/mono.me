using System.Collections.Concurrent;
using System.Diagnostics;
using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The bulk path of a seed: generated lines of annotations.jsonl written by the bulk writer in transactional batches.
public class BulkSeedTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
    private const int Customers = 2_000;

    [Fact]
    public async Task Seed_TenThousandGeneratedAnnotations_AreWrittenInBulkWithTheirRelationsAndAudits()
    {
        // 2 + 4 × 2 000 + 1 998 = 10 000 annotations: the last two executions have no unit of execution
        using var set = new TestSetFolder("bulk")
            .Write("set.json", """{ "Name": "bulk", "Project": "bulk" }""")
            .Write("annotations.jsonl", $$$"""
                {"AnnotationKey":"rst.payments","Title":"Payments"}
                {"AnnotationKey":"unt.payments.settle","UnitType":"time","UnitDefinition":"0 * * * *"}
                {"$count":{{{Customers}}},"AnnotationKey":"sbt.c-{0000}","Title":"Customer {n}","Labels":["generated"],"Values":{"number":"C-{00000}"}}
                {"$count":{{{Customers}}},"AnnotationKey":"cnt.c-{0000}.prod"}
                {"$count":{{{Customers}}},"AnnotationKey":"usg.c-{0000}.payments"}
                {"$count":{{{Customers}}},"AnnotationKey":"exe.c-{0000}.payments.prod"}
                {"$count":{{{Customers - 2}}},"AnnotationKey":"uxe.c-{0000}.payments.prod.settle"}
                """);
        var (organization, owner) = (NewOrganization(), NewOwner());
        var progress = new ConcurrentQueue<int>();
        var request = new DataSetSeedRequest
        {
            Paths = [set.Path],
            Organization = organization,
            Owner = owner,
            Options = new DataSetSeedOptions { AnnotationProgress = new SynchronousProgress(progress.Enqueue) },
        };

        var started = Stopwatch.GetTimestamp();
        var run = await Seeder.SeedAsync(request, Cancellation);
        var elapsed = Stopwatch.GetElapsedTime(started);
        Log($"10 000 annotations seeded in {elapsed.TotalSeconds:F1} s (the set {run.Sets.Single().Elapsed.TotalSeconds:F1} s) on the {Fixture.Emulator.Kind} emulator.");

        run.HasFailures.ShouldBeFalse();
        var result = run.Sets.ShouldHaveSingleItem();
        result.IsRecorded.ShouldBeTrue();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary());
        var lines = result.Lines.ShouldNotBeNull();
        (lines.Lines, lines.GeneratorLines, lines.Annotations).ShouldBe((7, 5, 10_000));
        lines.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 10_000 });
        lines.AncestorsCreated.ShouldBeEmpty();
        progress.ShouldNotBeEmpty();
        progress.Last().ShouldBe(10_000);

        // every annotation is active
        var target = run.Target;
        var keys = Enumerable.Range(1, Customers)
            .SelectMany(index => new[] { $"sbt.c-{index:0000}", $"cnt.c-{index:0000}.prod", $"usg.c-{index:0000}.payments", $"exe.c-{index:0000}.payments.prod" })
            .Concat(Enumerable.Range(1, Customers - 2).Select(index => $"uxe.c-{index:0000}.payments.prod.settle"))
            .Append("rst.payments")
            .Append("unt.payments.settle")
            .Select(AnnotationKey.Parse)
            .ToList();
        keys.Count.ShouldBe(10_000);
        (await Annotations.FindActiveAnnotationKeysAsync(organization, target.Project, target.View, keys, Cancellation)).Count.ShouldBe(10_000);

        // the relation arrays of a sample, as a create of each would leave them
        var payments = (Responsibility)(await Annotations.GetAnnotationAsync(Key(target, "rst.payments")))!;
        payments.UnitNames.ShouldBe(["settle"]);

        foreach (var index in new[] { 1, 42, 1_500, Customers - 2 })
        {
            var subject = (Subject)(await Annotations.GetAnnotationAsync(Key(target, $"sbt.c-{index:0000}")))!;
            (subject.Title, subject.Values!["number"].ToString()).ShouldBe(($"Customer {index}", $"C-{index:00000}"));
            subject.ContextNames.ShouldBe(["prod"]);
            subject.ResponsibilityNames.ShouldBe(["payments"]);
            ((Context)(await Annotations.GetAnnotationAsync(Key(target, $"cnt.c-{index:0000}.prod")))!).ResponsibilityNames.ShouldBe(["payments"]);
            ((Usage)(await Annotations.GetAnnotationAsync(Key(target, $"usg.c-{index:0000}.payments")))!).ContextNames.ShouldBe(["prod"]);
            ((Execution)(await Annotations.GetAnnotationAsync(Key(target, $"exe.c-{index:0000}.payments.prod")))!).UnitNames.ShouldBe(["settle"]);
        }

        ((Execution)(await Annotations.GetAnnotationAsync(Key(target, $"exe.c-{Customers:0000}.payments.prod")))!).UnitNames.ShouldBeEmpty();

        // one audit entry per annotation, as a create writes it
        foreach (var key in new[] { "rst.payments", "unt.payments.settle", "sbt.c-0001", "cnt.c-0777.prod", "usg.c-2000.payments", "exe.c-1234.payments.prod", "uxe.c-1998.payments.prod.settle" })
        {
            var entry = (await Annotations.GetAnnotationHistoryAsync(Key(target, key)))!.Entries.ShouldHaveSingleItem(key);
            (entry.Action, entry.Actor, entry.Cause).ShouldBe((AnnotationAuditAction.Created, AuditActor.System, (AnnotationAuditCause?)null), key);
        }

        var record = await Records.GetAsync(target, set.Name, Cancellation);
        (record!.Annotations, record.Created).ShouldBe((10_000, 10_000));

        // a generous bound: on the vNext emulator the seed takes well under a minute
        elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(10));
    }

    // Progress<T> posts to the thread pool, so its last report could come after the seed returns.
    private sealed class SynchronousProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
