using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// A seed of a set folder (--path) end to end: the target, the documents through the importer, annotations.jsonl
// through the bulk writer, the record, and what a second, partial, changed or dry run does.
public class SeedTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
    private const string Project = "ledger";

    private static readonly string[] FileAnnotations =
    [
        "rst.billing",
        "unt.billing.invoice",
        "sbt.acme",
        "cnt.acme.prod",
        "usg.acme.billing",
        "exe.acme.billing.prod",
        "uxe.acme.billing.prod.invoice",
    ];

    private static readonly string[] LineAnnotations =
    [
        "sbt.globex",
        "sbt.customer-01",
        "sbt.customer-02",
        "sbt.customer-03",
        "cnt.customer-01.prod",
        "cnt.customer-02.prod",
        "cnt.customer-03.prod",
        "usg.customer-01.billing",
        "usg.customer-02.billing",
        "usg.customer-03.billing",
    ];

    [Fact]
    public async Task Seed_CreatesTheTargetTheOwnersAccessTheViewAndTheDocuments()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());

        var run = await Seeder.SeedAsync(Request(set, organization, owner, view: "staging"), Cancellation);

        run.HasFailures.ShouldBeFalse();
        run.Target.ShouldBe(new DataSetTarget(organization, Project, "staging"));
        run.Provisioning.ShouldBe(new DataSetProvisioning(IsAccountCreated: true, IsOrganizationCreated: true, IsProjectCreated: true, IsViewRegistered: true));

        var result = run.Sets.ShouldHaveSingleItem();
        result.IsSeeded.ShouldBeTrue();
        result.IsRecorded.ShouldBeTrue();
        result.Items.ShouldBeEmpty();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 13 });
        result.Lines!.Annotations.ShouldBe(10);
        result.Lines.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 10 });
        result.Lines.AncestorsCreated.ShouldBeEmpty();

        // the owner owns the organization and the project, in the access points and in its account
        (await Access.GetAccessPointAsync(organization))!.AccessMap[owner].ShouldBe(AccountRole.Owner);
        var project = await Access.GetAccessPointAsync($"{organization}.{Project}");
        project!.AccessMap[owner].ShouldBe(AccountRole.Owner);
        project.Description.ShouldBe("Seeded by the integration tests of DbCreator.");
        var account = await Access.GetAccountAsync(owner);
        (account!.UserName, account.Name).ShouldBe((owner, owner));
        (await Access.GetAccountRoleAsync(owner, $"{organization}.{Project}")).ShouldBe(AccountRole.Owner);

        var view = (await Views.GetViewsAsync(organization, Project, discover: false)).Single(view => view.Name == "staging");
        (view.IsRegistered, view.Description).ShouldBe((true, "The view of the seed."));

        // the annotations of the files and the lines in the view, none in the default view
        (await Annotations.GetViewAnnotationsAsync(organization, Project, "staging", Cancellation))
            .Select(annotation => annotation.AnnotationKey)
            .Order(StringComparer.Ordinal)
            .ShouldBe(FileAnnotations.Concat(LineAnnotations).Order(StringComparer.Ordinal));
        (await Annotations.GetViewAnnotationsAsync(organization, Project, Storyteller.Constants.DefaultViewName, Cancellation)).ShouldBeEmpty();

        // the relation arrays of a create, files and lines together
        var target = run.Target;
        var billing = (Responsibility)(await Annotations.GetAnnotationAsync(Key(target, "rst.billing")))!;
        billing.Title.ShouldBe("Billing");
        billing.UnitNames.ShouldBe(["invoice"]);
        var acme = (Subject)(await Annotations.GetAnnotationAsync(Key(target, "sbt.acme")))!;
        acme.ContextNames.ShouldBe(["prod"], ignoreOrder: true);
        acme.ResponsibilityNames.ShouldBe(["billing"], ignoreOrder: true);
        acme.Values!["since"].ToString().ShouldBe("2024-01-01");
        ((Execution)(await Annotations.GetAnnotationAsync(Key(target, "exe.acme.billing.prod")))!).UnitNames.ShouldBe(["invoice"], ignoreOrder: true);
        var customer = (Subject)(await Annotations.GetAnnotationAsync(Key(target, "sbt.customer-02")))!;
        (customer.Title, customer.Labels!.Single()).ShouldBe(("Customer 2", "generated"));
        customer.ContextNames.ShouldBe(["prod"], ignoreOrder: true);
        customer.ResponsibilityNames.ShouldBe(["billing"], ignoreOrder: true);

        // one audit entry per annotation, by the System actor, from a file and from a line alike, and one more for the
        // configuration of an annotation that has one
        foreach (var key in new[] { "uxe.acme.billing.prod.invoice", "sbt.globex", "usg.customer-03.billing" })
        {
            var entry = (await Annotations.GetAnnotationHistoryAsync(Key(target, key)))!.Entries.ShouldHaveSingleItem(key);
            (entry.Action, entry.Actor, entry.Cause).ShouldBe((AnnotationAuditAction.Created, AuditActor.System, (AnnotationAuditCause?)null), key);
        }

        (await Annotations.GetAnnotationHistoryAsync(Key(target, "rst.billing")))!.Entries
            .Select(entry => (entry.Action, entry.Actor))
            .ShouldBe([(AnnotationAuditAction.ConfigurationChanged, AuditActor.System), (AnnotationAuditAction.Created, AuditActor.System)]);

        // the schemas, the template and the configurations
        (await Schemas.GetSchemaAsync(organization, Project, "staging", "exe")).ShouldNotBeNull();
        (await Schemas.GetDescendantTypeSchemaAsync(organization, Project, "staging", "rst.billing", "usg")).ShouldNotBeNull();
        (await Templates.GetTemplateAsync(organization, Project, "staging", "exe")).ShouldNotBeNull();
        var configuration = await Configurations.GetRawConfigurationAsync(Key(target, "rst.billing"));
        configuration!.Content.ToString(Formatting.None).ShouldBe("""{"currency":"EUR","retries":3}""");
        configuration.Modified.By.ShouldBe(AuditActor.System);
        (await OwnConfigurationAsync(Key(target, "exe.acme.billing.prod"))).ShouldBe("""{"replicas":2}""");

        // the record of the set
        var record = await Records.GetAsync(target, set.Name, Cancellation);
        record.ShouldNotBeNull();
        (record.Set, record.View, record.Version, record.Hash).ShouldBe((set.Name, "staging", 1, result.Hash));
        (record.Documents, record.Annotations, record.Created, record.Skipped).ShouldBe((13, 10, 23, 0));
        record.Seeded.By.ShouldBe(AuditActor.System);
    }

    [Fact]
    public async Task Seed_Again_ReportsTheSetSeededAndWritesNothing()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());
        (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).HasFailures.ShouldBeFalse();
        var stored = await StoredItems.ReadAsync(Services, organization, Project, owner);

        var again = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        again.Provisioning.ShouldBe(new DataSetProvisioning(false, false, false, false));
        var result = again.Sets.ShouldHaveSingleItem();
        (result.IsAlreadySeeded, result.IsRecorded, result.Documents, result.Lines).ShouldBe((true, false, (ViewImportTally?)null, (DataSetLinesResult?)null));

        // and through the command, as the tool runs it: success, seeded already
        var command = Command();
        command.Paths = [set.Path];
        (command.Organization, command.Project, command.Owner) = (organization, Project, owner);
        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBe(stored);
    }

    [Fact]
    public async Task Seed_APartlySeededTarget_IsCompletedWithoutOverwriting()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());
        var target = new DataSetTarget(organization, Project, Storyteller.Constants.DefaultViewName);
        var someone = AuditActor.Account("someone");

        // the project, an annotation of a file and one of a line (with the defaults, not the set's titles), and a
        // configuration the set has otherwise
        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = Project, OwnerId = owner });
        await Annotations.CreateAnnotationsFromStringAsync(organization, Project, target.View, ["rst.billing", "sbt.globex"], someone);
        await Configurations.CreateOrUpdateConfigurationAsync(Key(target, "rst.billing"), JObject.Parse("""{"currency":"CZK"}"""), someone);

        var run = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        run.HasFailures.ShouldBeFalse();
        run.Provisioning.ShouldBe(new DataSetProvisioning(IsAccountCreated: true, IsOrganizationCreated: false, IsProjectCreated: false, IsViewRegistered: false));
        var result = run.Sets.ShouldHaveSingleItem();
        result.IsRecorded.ShouldBeTrue();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 11, Skipped = 2 });
        result.Lines!.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 9, Skipped = 1 });
        result.Items
            .Select(item => (item.Path, item.Outcome, item.ErrorCode))
            .ShouldBe(
            [
                ("annotations/rst.billing.json", ViewImportOutcome.Skipped, ErrorCodes.DocumentExists),
                ("annotations.jsonl", ViewImportOutcome.Skipped, ErrorCodes.DocumentExists),
                ("configurations/rst.billing.conf.json", ViewImportOutcome.Skipped, ErrorCodes.DocumentExists),
            ],
            ignoreOrder: true);

        // what existed stays as it was, by whom it was; what was missing is there, and the new unit joins the
        // existing responsibility
        var billing = (Responsibility)(await Annotations.GetAnnotationAsync(Key(target, "rst.billing")))!;
        billing.Title.ShouldBeNull();
        billing.UnitNames.ShouldBe(["invoice"]);
        (await Annotations.GetAnnotationHistoryAsync(Key(target, "rst.billing")))!.Entries.ShouldAllBe(entry => entry.Actor == someone);
        (await Annotations.GetAnnotationAsync(Key(target, "sbt.globex")))!.Title.ShouldBeNull();
        (await Configurations.GetRawConfigurationAsync(Key(target, "rst.billing")))!.Content.ToString(Formatting.None).ShouldBe("""{"currency":"CZK"}""");
        (await Annotations.GetAnnotationAsync(Key(target, "uxe.acme.billing.prod.invoice"))).ShouldNotBeNull();
        (await Annotations.GetAnnotationAsync(Key(target, "usg.customer-03.billing"))).ShouldNotBeNull();
        (await OwnConfigurationAsync(Key(target, "exe.acme.billing.prod"))).ShouldBe("""{"replicas":2}""");
    }

    [Fact]
    public async Task Seed_AfterARunThatStoppedHalfway_CreatesWhatIsMissing()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());
        var target = new DataSetTarget(organization, Project, Storyteller.Constants.DefaultViewName);
        (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).HasFailures.ShouldBeFalse();

        // a run that stopped halfway: no record, an annotation of a file and one of a line never written, a
        // configuration without content
        var repository = Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(organization);
        await repository.Container.DeleteItemAsync<object>(
            DataSetSeedRecord.GetId(target.View, set.Name),
            repository.Key(DataSetSeedRecord.GetPartitionKey(Project)),
            cancellationToken: Cancellation);

        foreach (var key in new[] { "uxe.acme.billing.prod.invoice", "cnt.customer-03.prod" })
        {
            await Annotations.DeleteAnnotationAsync(Key(target, key), AuditActor.System);
            await Annotations.PurgeAnnotationAsync(Key(target, key), AuditActor.System);
            (await Annotations.GetAnnotationHistoryAsync(Key(target, key))).ShouldBeNull(key);
        }

        await Configurations.ClearConfigurationAsync(Key(target, "usg.acme.billing"), AuditActor.System);

        var run = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        run.HasFailures.ShouldBeFalse();
        var result = run.Sets.ShouldHaveSingleItem();
        result.IsAlreadySeeded.ShouldBeFalse();
        result.IsRecorded.ShouldBeTrue();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 2, Unchanged = 11 });
        result.Lines!.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 1, Unchanged = 9 });

        ((Execution)(await Annotations.GetAnnotationAsync(Key(target, "exe.acme.billing.prod")))!).UnitNames.ShouldBe(["invoice"]);
        ((Subject)(await Annotations.GetAnnotationAsync(Key(target, "sbt.customer-03")))!).ContextNames.ShouldBe(["prod"]);
        (await OwnConfigurationAsync(Key(target, "usg.acme.billing"))).ShouldBe("""{"plan":"standard"}""");
        (await Records.GetAsync(target, set.Name, Cancellation)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Seed_AChangedSet_AddsOnlyWhatIsMissing()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());
        var target = new DataSetTarget(organization, Project, Storyteller.Constants.DefaultViewName);
        var first = (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).Sets.Single();
        var stored = await StoredItems.ReadAsync(Services, organization, Project, owner);

        // a new annotation file, and another title in a file seeded already
        set.Write("annotations/cnt.acme.staging.json", """{ "Title": "Staging" }""")
            .Write("annotations/rst.billing.json", """{ "Title": "Billing and invoicing" }""");

        var run = await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        run.HasFailures.ShouldBeFalse();
        var result = run.Sets.ShouldHaveSingleItem();
        result.IsAlreadySeeded.ShouldBeFalse();
        result.Hash.ShouldNotBe(first.Hash);
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 1, Unchanged = 12, Skipped = 1 });
        result.Lines!.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Unchanged = 10 });
        (await Annotations.GetAnnotationAsync(Key(target, "rst.billing")))!.Title.ShouldBe("Billing");
        ((Subject)(await Annotations.GetAnnotationAsync(Key(target, "sbt.acme")))!).ContextNames.ShouldBe(["prod", "staging"], ignoreOrder: true);
        (await Records.GetAsync(target, set.Name, Cancellation))!.Hash.ShouldBe(result.Hash);

        // written: the new annotation (and its audit), the relation array of its subject, and the record; nothing else
        var changed = (await StoredItems.ReadAsync(Services, organization, Project, owner))
            .Where(item => !stored.TryGetValue(item.Key, out var etag) || etag != item.Value)
            .Select(item => item.Key)
            .ToList();
        stored.Keys.ShouldBeSubsetOf((await StoredItems.ReadAsync(Services, organization, Project, owner)).Keys);
        changed.ShouldContain(key => key.EndsWith("/default.cnt.acme.staging", StringComparison.Ordinal));
        changed.ShouldContain(key => key.EndsWith("/default.sbt.acme", StringComparison.Ordinal));
        changed.ShouldContain(key => key.EndsWith($"/{DataSetSeedRecord.GetId(target.View, set.Name)}", StringComparison.Ordinal));
        changed.ShouldAllBe(key => key.Contains("cnt.acme.staging") || key.EndsWith("/default.sbt.acme") || key.EndsWith($"/{DataSetSeedRecord.GetId(target.View, set.Name)}"));
    }

    [Fact]
    public async Task Seed_DryRun_WritesNothing()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner) = (NewOrganization(), NewOwner());

        var run = await Seeder.SeedAsync(Request(set, organization, owner, view: "staging") with { Options = new DataSetSeedOptions { DryRun = true } }, Cancellation);

        run.IsDryRun.ShouldBeTrue();
        run.HasFailures.ShouldBeFalse();
        run.Provisioning.ShouldBe(new DataSetProvisioning(true, true, true, true));
        var result = run.Sets.ShouldHaveSingleItem();
        result.IsRecorded.ShouldBeFalse();
        result.Documents!.Summary.ShouldBe(new ViewImportSummary { Created = 13 });
        result.Lines!.Summary.ShouldBe(new ViewImportSummary { Content = ViewArchiveContent.Annotations, Created = 10 });

        // no account, access point, view, document or record
        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBeEmpty();
        (await Access.GetAccountAsync(owner)).ShouldBeNull();
        (await Access.GetAccessPointAsync(organization)).ShouldBeNull();
        (await Records.GetAsync(run.Target, set.Name, Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task Seed_AnOrganizationOfAnotherOwner_IsRefusedBeforeAnyWrite()
    {
        using var set = TestSetFolder.Accounting();
        var (organization, owner, intruder) = (NewOrganization(), NewOwner(), NewOwner());
        await Access.CreateAccessPointAsync(new AccessPointCreate { Organization = organization, Project = "main", OwnerId = owner });
        var stored = await StoredItems.ReadAsync(Services, organization, "main", owner);

        await Should.ThrowAsync<DataSetException>(() => Seeder.SeedAsync(Request(set, organization, intruder), Cancellation));

        var command = Command();
        command.Paths = [set.Path];
        (command.Organization, command.Project, command.Owner) = (organization, Project, intruder);
        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        (await Access.GetAccessPointAsync($"{organization}.{Project}")).ShouldBeNull();
        (await Access.GetAccountAsync(intruder)).ShouldBeNull();
        (await Access.GetAccessPointAsync(organization))!.AccessMap.Keys.ShouldBe([owner]);
        (await StoredItems.ReadAsync(Services, organization, "main", owner)).ShouldBe(stored);
    }

    // The current version of the annotation's own configuration, without what it inherits.
    private async Task<string?> OwnConfigurationAsync(FullKey key)
    {
        var configuration = await Configurations.GetRawConfigurationAsync(key);
        return configuration is null
            ? null
            : (await Configurations.GetConfigurationVersionContentAsync(key, (uint)configuration.Version))?.Content.ToString(Formatting.None);
    }

    private static DataSetSeedRequest Request(TestSetFolder set, string organization, string owner, string? view = null)
    {
        return new DataSetSeedRequest
        {
            Paths = [set.Path],
            Organization = organization,
            Project = Project,
            View = view,
            Owner = owner,
        };
    }
}
