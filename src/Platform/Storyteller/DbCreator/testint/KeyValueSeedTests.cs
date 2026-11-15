using System.Text;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The pairs of key-values.json end to end: through the key/value import of the tool's own registrations into the
// emulator, Public pairs without a vault and Standard pairs with Lowkey Vault, and what a second, changed or dry run
// does with existing and deleted pairs.
public class KeyValueSeedTests(DbCreatorFixture fixture) : SeedTestsBase(fixture)
{
    private const string Project = "values";
    private const string View = Storyteller.Constants.DefaultViewName;
    private const string SecretValue = "demo-only-value-7f3a";

    private const string PublicPairs = """
        {
          "Format": "2s.key-values.v1",
          "Items": [
            { "Key": "dunning.maxReminders", "Protection": "Public", "Value": "3" },
            { "Key": "features.flags", "Protection": "Public", "Value": "{\"ksef\":true}", "ContentType": "application/json" },
            { "Key": "peppol.apiKeyRef", "Protection": "Public", "Value": "kv://fabrikam-prod/peppol-api-key", "ContentType": "application/vnd.2s.secret-reference" },
            { "Key": "smtp.host", "Protection": "Public", "Value": "smtp.fabrikam.example", "ContentType": "text/plain" }
          ]
        }
        """;

    private static readonly string StandardPairs = $$"""
        {
          "Items": [
            { "Key": "smtp.host", "Protection": "Public", "Value": "smtp.fabrikam.example" },
            { "Key": "smtp.password", "Protection": "Standard", "Value": "{{SecretValue}}" }
          ]
        }
        """;

    [Fact]
    public async Task PublicPairs_SeedWithoutAVault_AndASeedAgainChangesNothing()
    {
        using var set = PairsSet(PublicPairs);
        var (organization, owner) = (NewOrganization(), NewOwner());

        var result = (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).Sets.Single();

        result.Failures.ShouldBeEmpty();
        result.IsRecorded.ShouldBeTrue();
        result.Items.ShouldBeEmpty();
        var pairs = result.KeyValues.ShouldNotBeNull();
        pairs.Pairs.ShouldBe(4);
        pairs.Summary.ShouldBe(new ViewImportSummary { Created = 4 });

        // the pairs with their content types, written by the System actor
        var flags = await GetAsync(Services, organization, "features.flags");
        (flags.Value, flags.ContentType, flags.Version).ShouldBe(("""{"ksef":true}""", "application/json", 1ul));
        flags.Created.By.ShouldBe(AuditActor.System);
        (await GetAsync(Services, organization, "peppol.apiKeyRef")).IsReference.ShouldBeTrue();
        (await GetAsync(Services, organization, "smtp.host")).ContentType.ShouldBe("text/plain");

        // the record counts the pairs
        var record = (await Records.GetAsync(new DataSetTarget(organization, Project, View), set.Name, Cancellation)).ShouldNotBeNull();
        (record.Documents, record.KeyValues, record.Created).ShouldBe((0, 4, 4));

        // a seed again plans the pairs and writes none of them, not even the audit event of an import; only the record
        // is written again
        var stored = await StoredItems.ReadOrganizationAsync(Services, organization);
        var again = (await Seeder.SeedAsync(Request(set, organization, owner) with { Options = new DataSetSeedOptions { Again = true } }, Cancellation)).Sets.Single();

        again.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Unchanged = 4 });
        Changed(stored, await StoredItems.ReadOrganizationAsync(Services, organization)).ShouldBe([RecordItem(set)]);
    }

    [Fact]
    public async Task StandardPairs_WithoutAVault_AreRefusedBeforeAnythingIsWritten()
    {
        using var set = PairsSet(StandardPairs).Write("annotations/rst.billing.json", "{}");
        var (organization, owner) = (NewOrganization(), NewOwner());

        var exception = await Should.ThrowAsync<DataSetException>(() => Seeder.SeedAsync(Request(set, organization, owner), Cancellation));
        exception.Message.ShouldContain("has 1 Standard pair in key-values.json, and DbCreator has no Standard vault");

        (await RunAsync(Command(), set, organization, owner)).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await RunAsync(Command(), set, organization, owner, dryRun: true)).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBeEmpty();
    }

    [Fact]
    public async Task StandardPairs_SeedWithLowkeyVault()
    {
        var services = await Fixture.GetVaultServicesAsync(Cancellation);
        using var set = PairsSet(StandardPairs);
        var (organization, owner) = (NewOrganization(), NewOwner());

        (await RunAsync(Command(services), set, organization, owner)).ShouldBe(ExitCodes.SUCCESS);

        // the secret reveals, and the item stores its envelope, not the value
        var password = await GetAsync(services, organization, "smtp.password", reveal: true);
        (password.Protection, password.Value).ShouldBe((KeyValueProtection.Standard, SecretValue));
        var container = services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(organization).Container;
        using var response = await container.ReadItemStreamAsync("kv.smtp.password", PartitionKeys.GetCosmosKeyValues(organization, Project, View), cancellationToken: Cancellation);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(response.Content, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(Cancellation);
        raw.ShouldNotContain(SecretValue);
        raw.ShouldContain("RSA-OAEP-256");

        // the first Standard pair of the project records its key in the policy, as a write of the API does
        var policy = await services.GetRequiredService<IKeyValuePolicyStore>().GetCurrentAsync(organization, Project, Cancellation);
        policy.Standard.ShouldNotBeNull().KeyId.ShouldNotBeNullOrWhiteSpace();

        // a seed again decrypts the stored secret to compare it, and finds it unchanged
        var again = (await services.GetRequiredService<DataSetSeeder>().SeedAsync(
            Request(set, organization, owner) with { Options = new DataSetSeedOptions { Again = true } },
            Cancellation)).Sets.Single();
        again.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Unchanged = 2 });
    }

    [Fact]
    public async Task ExistingAndDeletedPairs_AreSkipped_AndAChangedSetCreatesOnlyTheNewPairs()
    {
        using var set = PairsSet("""
            { "Items": [
              { "Key": "a", "Protection": "Public", "Value": "1" },
              { "Key": "b", "Protection": "Public", "Value": "2" },
              { "Key": "c", "Protection": "Public", "Value": "3" }
            ] }
            """);
        var (organization, owner) = (NewOrganization(), NewOwner());
        await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        // another value for a, and b deleted, through the service as the API writes them
        var caller = KeyValueCaller.WithSecrets(AuditActor.Account(owner));
        await KeyValues.SetAsync(organization, Project, View, "A", new KeyValueWrite { Value = "changed" }, caller);
        (await KeyValues.DeleteAsync(organization, Project, View, "b", new KeyValueDeleteOptions(), caller)).ShouldBeTrue();
        var deleted = await KeyValues.GetAsync(organization, Project, View, "b", new KeyValueReadOptions { IncludeDeleted = true }, caller);

        // the changed set: d is new, the others as they were
        set.Write("key-values.json", """
            { "Items": [
              { "Key": "a", "Protection": "Public", "Value": "1" },
              { "Key": "b", "Protection": "Public", "Value": "2" },
              { "Key": "c", "Protection": "Public", "Value": "3" },
              { "Key": "d", "Protection": "Public", "Value": "4" }
            ] }
            """);

        var result = (await Seeder.SeedAsync(Request(set, organization, owner), Cancellation)).Sets.Single();

        result.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 1, Unchanged = 1, Skipped = 2 });
        result.Items.Select(item => (item.KeyValueKey, item.Outcome, item.ErrorCode)).ShouldBe(
        [
            ("a", ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueExists),
            ("b", ViewImportOutcome.Skipped, DataSetKeyValueSeeder.KeyValueDeleted),
        ]);

        // skipped pairs are no failure; nothing of a or b changed, and b stays deleted
        result.IsRecorded.ShouldBeTrue();
        (await GetAsync(Services, organization, "a")).ShouldSatisfyAllConditions(
            pair => pair.Value.ShouldBe("changed"),
            pair => pair.Version.ShouldBe(2ul));
        (await KeyValues.GetAsync(organization, Project, View, "b", new KeyValueReadOptions(), caller)).ShouldBeNull();
        var still = (await KeyValues.GetAsync(organization, Project, View, "b", new KeyValueReadOptions { IncludeDeleted = true }, caller)).ShouldNotBeNull();
        (still.IsDeleted, still.Version, still.Modified).ShouldBe((true, deleted!.Version, deleted.Modified));
        (await GetAsync(Services, organization, "d")).Value.ShouldBe("4");
    }

    [Fact]
    public async Task DryRun_DecidesEveryPairAndWritesNothing()
    {
        using var set = PairsSet(PublicPairs);
        var (organization, owner) = (NewOrganization(), NewOwner());

        // into a new target: every pair would be created, nothing is
        var dryRun = (await Seeder.SeedAsync(Request(set, organization, owner) with { Options = new DataSetSeedOptions { DryRun = true } }, Cancellation)).Sets.Single();

        dryRun.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 4 });
        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBeEmpty();

        // into a seeded target with a changed pair: the decisions of a real run, and still nothing written
        await Seeder.SeedAsync(Request(set, organization, owner), Cancellation);
        await KeyValues.SetAsync(organization, Project, View, "smtp.host", new KeyValueWrite { Value = "smtp.other.example" }, KeyValueCaller.System());
        var stored = await StoredItems.ReadAsync(Services, organization, Project, owner);

        var again = (await Seeder.SeedAsync(Request(set, organization, owner) with { Options = new DataSetSeedOptions { DryRun = true, Again = true } }, Cancellation)).Sets.Single();

        again.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Unchanged = 3, Skipped = 1 });
        again.Items.ShouldHaveSingleItem().ErrorCode.ShouldBe(DataSetKeyValueSeeder.KeyValueExists);
        (await StoredItems.ReadAsync(Services, organization, Project, owner)).ShouldBe(stored);
    }

    [Theory]
    [InlineData("""{ "Key": "api.token", "Protection": "Hsm", "Value": "x" }""")]
    [InlineData("""{ "Key": "api.token", "Protection": "Byok", "Value": "x" }""")]
    [InlineData("""{ "Key": "api.token", "Protection": "Hyok", "Value": "x" }""")]
    [InlineData("""{ "Key": "db.password", "Protection": "Standard" }""")]
    public async Task RefusedLevelsAndItemsWithoutAValue_ExitWithTwoAndWriteNothing(string item)
    {
        // with the vault: the refusals hold for a tool that can encrypt Standard pairs too
        var services = await Fixture.GetVaultServicesAsync(Cancellation);
        using var set = PairsSet($$"""{ "Items": [ { "Key": "smtp.host", "Protection": "Public", "Value": "x" }, {{item}} ] }""");
        var (organization, owner) = (NewOrganization(), NewOwner());

        (await RunAsync(Command(services), set, organization, owner)).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        (await StoredItems.ReadAsync(services, organization, Project, owner)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AProjectPolicyWithoutStandard_FailsTheStandardPair_AndTheOthersAreCreated()
    {
        var services = await Fixture.GetVaultServicesAsync(Cancellation);
        var seeder = services.GetRequiredService<DataSetSeeder>();
        var (organization, owner) = (NewOrganization(), NewOwner());

        // the project exists, and its policy allows Public pairs only
        using (var first = PairsSet("""{ "Items": [ { "Key": "app.name", "Protection": "Public", "Value": "Invoicing" } ] }"""))
        {
            await seeder.SeedAsync(Request(first, organization, owner), Cancellation);
        }

        await services.GetRequiredService<IKeyValuePolicyService>().UpdateAsync(
            organization,
            Project,
            new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public] },
            KeyValueCaller.System(),
            Cancellation);

        using var set = PairsSet(StandardPairs, name: "secrets");
        var run = await seeder.SeedAsync(Request(set, organization, owner), Cancellation);

        var result = run.Sets.Single();
        var failure = result.Failures.ShouldHaveSingleItem();
        (failure.KeyValueKey, failure.Protection, failure.ErrorCode).ShouldBe(("smtp.password", KeyValueProtection.Standard, ErrorCodes.ProtectionNotAllowed));
        result.KeyValues!.Summary.ShouldBe(new ViewImportSummary { Created = 1, Failed = 1 });
        (await GetAsync(services, organization, "smtp.host")).Value.ShouldBe("smtp.fabrikam.example");
        ExitCodes.Of(run).ShouldBe(ExitCodes.ERROR_SEED_FAILED);
        result.IsRecorded.ShouldBeFalse();
    }

    [Fact]
    public async Task ABindingToASeededPair_ResolvesInAResolvedRead()
    {
        var services = await Fixture.GetVaultServicesAsync(Cancellation);
        using var set = PairsSet(StandardPairs)
            .Write("annotations/rst.billing.json", "{}")
            .Write("configurations/rst.billing.conf.json", """
                {
                  "host": "@smtp.host",
                  "url": "@[https://@smtp.host/api]",
                  "onlyPublic": "@public(smtp.host)",
                  "password": "@(smtp.password, secret)"
                }
                """);
        var (organization, owner) = (NewOrganization(), NewOwner());

        var result = (await services.GetRequiredService<DataSetSeeder>().SeedAsync(Request(set, organization, owner), Cancellation)).Sets.Single();
        result.Failures.ShouldBeEmpty();

        var configurations = services.GetRequiredService<IConfigurationService>();
        var key = Key(new DataSetTarget(organization, Project, View), "rst.billing");
        var resolved = (await configurations.GetResolvedConfigurationWithSecretsAsync(key)).ShouldNotBeNull();

        resolved.Content["host"]!.ToString().ShouldBe("smtp.fabrikam.example");
        resolved.Content["url"]!.ToString().ShouldBe("https://smtp.fabrikam.example/api");
        resolved.Content["onlyPublic"]!.ToString().ShouldBe("smtp.fabrikam.example");
        resolved.Content["password"]!.ToString().ShouldBe(SecretValue);
    }

    private static TestSetFolder PairsSet(string keyValues, string name = "values-set")
    {
        return new TestSetFolder(name)
            .Write("set.json", $$"""{ "Name": "{{name}}", "Description": "Pairs of the integration tests of DbCreator." }""")
            .Write("key-values.json", keyValues);
    }

    private static DataSetSeedRequest Request(TestSetFolder set, string organization, string owner)
    {
        return new DataSetSeedRequest
        {
            Paths = [set.Path],
            Organization = organization,
            Project = Project,
            Owner = owner,
        };
    }

    private static string RecordItem(TestSetFolder set) => $"{DataSetSeedRecord.GetPartitionKey(Project)}/{DataSetSeedRecord.GetId(View, set.Name)}";

    // The items whose ETag changed or that are new.
    private static List<string> Changed(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        return after.Where(item => !before.TryGetValue(item.Key, out var etag) || etag != item.Value).Select(item => item.Key).ToList();
    }

    private static async Task<int> RunAsync(Commands.SeedCommand command, TestSetFolder set, string organization, string owner, bool dryRun = false)
    {
        command.Paths = [set.Path];
        (command.Organization, command.Project, command.Owner, command.IsDryRun) = (organization, Project, owner, dryRun);
        return await command.OnExecuteAsync(Cancellation);
    }

    private static async Task<KeyValue> GetAsync(IServiceProvider services, string organization, string key, bool reveal = false)
    {
        var pair = await services.GetRequiredService<IKeyValueService>().GetAsync(
            organization,
            Project,
            View,
            key,
            new KeyValueReadOptions { Reveal = reveal },
            KeyValueCaller.System());
        return pair.ShouldNotBeNull(key);
    }
}
