#:package Microsoft.Azure.Cosmos
#:package Newtonsoft.Json
#:property PublishAot=false
#:property EnableStyleCop=false
#:property Nullable=enable

// Probe of hierarchical partition keys for the shared organization container of Storyteller.
// It creates its own database (hpk-probe-<time>), a container partitioned by /Organization then /PartitionKey,
// runs every kind of operation the Cosmos backend uses against it, prints one line per check and deletes the
// database again (unless --keep). Nothing outside its own database is read or changed.
//
//   dotnet run src/Platform/Storyteller/scripts/hpk-probe/hpk-probe.cs -- --endpoint https://localhost:8081/
//
// See docs/Platform/Storyteller/cookbooks/hierarchical-partition-key-probe.md.
using System.Diagnostics;
using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

var options = ProbeOptions.Parse(args);

if (options is null)
{
    Console.WriteLine("""
        Usage: dotnet run hpk-probe.cs -- [options]

          --endpoint <url>       Account endpoint (default https://localhost:8081/)
          --key <key>            Account key (default: the well-known emulator key)
          --throughput <mode>    Database throughput: autoscale (1000 max, default), manual (400) or none
          --insecure             Accept any TLS certificate (default for localhost and 127.0.0.1)
          --keep                 Keep the probe database for a look in the Data Explorer
          --label <text>         A name for this run in the summary, e.g. "classic emulator"
        """);
    return 2;
}

var results = new List<CheckResult>();
var databaseId = $"hpk-probe-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

Console.WriteLine($"Endpoint:  {options.Endpoint}");
Console.WriteLine($"Database:  {databaseId} (throughput: {options.Throughput})");
Console.WriteLine($"Run:       {options.Label}");
Console.WriteLine();

using var client = CreateClient(options, EmulatorKey);
Database? database = null;

try
{
    database = await CreateDatabaseAsync(client, databaseId, options.Throughput, results);
    var container = await CreateContainerAsync(database, results);

    if (container is not null)
    {
        await RunChecksAsync(container, results);
        await ListResourcesAsync(client, database, results);
    }
}
catch (Exception exception)
{
    results.Add(CheckResult.Fail("unexpected", 0, exception.GetType().Name + ": " + FirstLine(exception.Message)));
}
finally
{
    if (database is not null && !options.Keep)
    {
        try
        {
            await database.DeleteAsync();
            Console.WriteLine($"Database {databaseId} deleted.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Could not delete the database {databaseId}: {FirstLine(exception.Message)}");
        }
    }
    else if (database is not null)
    {
        Console.WriteLine($"Database {databaseId} kept (--keep). Delete it when you are done.");
    }
}

Console.WriteLine();
Console.WriteLine($"Summary of '{options.Label}': {results.Count(r => r.Outcome == "PASS")} passed, {results.Count(r => r.Outcome == "FAIL")} failed, {results.Count(r => r.Outcome == "INFO")} informational.");
return results.Count(r => r.Outcome == "FAIL");

static CosmosClient CreateClient(ProbeOptions options, string emulatorKey)
{
    var clientOptions = new CosmosClientOptions
    {
        // Every emulator speaks gateway mode; LimitToEndpoint keeps the SDK on the published port.
        ConnectionMode = ConnectionMode.Gateway,
        LimitToEndpoint = true,
        RequestTimeout = TimeSpan.FromSeconds(30),
        ApplicationName = "storyteller-hpk-probe",
    };

    if (options.Insecure)
    {
        clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });
    }

    return new CosmosClient(options.Endpoint, options.Key ?? emulatorKey, clientOptions);
}

static async Task<Database> CreateDatabaseAsync(CosmosClient client, string databaseId, string throughput, List<CheckResult> results)
{
    var properties = throughput switch
    {
        "manual" => ThroughputProperties.CreateManualThroughput(400),
        "none" => null,
        _ => ThroughputProperties.CreateAutoscaleThroughput(1000),
    };

    var response = await client.CreateDatabaseAsync(databaseId, properties);
    results.Add(Print(CheckResult.Pass("database with shared throughput", response.RequestCharge, throughput)));
    return response.Database;
}

static async Task<Container?> CreateContainerAsync(Database database, List<CheckResult> results)
{
    // A control: the layout of today (one path), so a failure below is about the hierarchical key and nothing else.
    try
    {
        var control = await database.CreateContainerAsync(new ContainerProperties("control", "/PartitionKey"));
        results.Add(Print(CheckResult.Pass("control: create container with /PartitionKey", control.RequestCharge)));
    }
    catch (CosmosException exception)
    {
        results.Add(Print(CheckResult.Fail("control: create container with /PartitionKey", exception.RequestCharge, $"{(int)exception.StatusCode} {FirstLine(exception.Message)}")));
    }

    // The layout proposed for the shared container: /Organization, then the PartitionKey the code uses today.
    var properties = new ContainerProperties("organizations", new List<string> { "/Organization", "/PartitionKey" })
    {
        DefaultTimeToLive = -1,
    };
    // As ContainerFactory.ApplySettings: everything indexed except the audit entries.
    properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
    properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/Entries/*" });

    try
    {
        var response = await database.CreateContainerAsync(properties);
        results.Add(Print(CheckResult.Pass("create container with /Organization, /PartitionKey", response.RequestCharge)));

        var read = await response.Container.ReadContainerAsync();
        var paths = read.Resource.PartitionKeyPaths ?? [];
        results.Add(Print(paths.Count == 2 && paths[0] == "/Organization" && paths[1] == "/PartitionKey"
            ? CheckResult.Pass("container reports both key paths", read.RequestCharge, string.Join(", ", paths))
            : CheckResult.Fail("container reports both key paths", read.RequestCharge, $"paths: {string.Join(", ", paths)}")));

        return response.Container;
    }
    catch (CosmosException exception)
    {
        results.Add(Print(CheckResult.Fail("create container with /Organization, /PartitionKey", exception.RequestCharge, $"{(int)exception.StatusCode} {FirstLine(exception.Message)}")));
        return null;
    }
}

static async Task RunChecksAsync(Container container, List<CheckResult> results)
{
    string[] organizations = ["acme", "globex"];
    string[] partitions = ["billing.kv.default", "billing.kv.prod", "portal.kv.default"];
    string[] ids = ["kv.db.host", "kv.db.password", "kvv.db.host.1"];

    // 1. The same ids in the same partitions of two organizations: an id is unique per full key only.
    double charge = 0;
    var created = 0;

    foreach (var organization in organizations)
    {
        foreach (var partition in partitions)
        {
            foreach (var id in ids)
            {
                var item = ProbeItem.Create(organization, partition, id);
                var response = await container.CreateItemAsync(item, Key(organization, partition));
                charge += response.RequestCharge;
                created++;
            }
        }
    }

    results.Add(Print(CheckResult.Pass("same id and PartitionKey in two organizations", charge, $"{created} items")));

    // 2. Point reads with the full key, and a miss across organizations.
    var read = await container.ReadItemAsync<ProbeItem>("kv.db.host", Key("acme", "billing.kv.default"));
    results.Add(Print(read.Resource.Organization == "acme"
        ? CheckResult.Pass("point read with the full key", read.RequestCharge)
        : CheckResult.Fail("point read with the full key", read.RequestCharge, $"got {read.Resource.Organization}")));

    try
    {
        await container.ReadItemAsync<ProbeItem>("kv.db.host", Key("initech", "billing.kv.default"));
        results.Add(Print(CheckResult.Fail("point read in another organization is a miss", 0, "found an item")));
    }
    catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
    {
        results.Add(Print(CheckResult.Pass("point read in another organization is a miss", exception.RequestCharge, "404")));
    }

    // 3. A single-partition query, as the listing of key/values of a view.
    var single = await QueryAsync<ProbeItem>(
        container,
        new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)").WithParameter("@prefix", "kv."),
        new QueryRequestOptions { PartitionKey = Key("acme", "billing.kv.default") });
    results.Add(Print(Expect("single-partition query (full key in the options)", single, 2, item => item.Organization == "acme" && item.PartitionKey == "billing.kv.default")));

    // 4. Organization-wide queries, as the listings of configurations and annotations of a project.
    var projectQuery = new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization AND STARTSWITH(c.PartitionKey, @project)")
        .WithParameter("@organization", "acme")
        .WithParameter("@project", "billing.");
    var prefixAndWhere = await QueryAsync<ProbeItem>(container, projectQuery, new QueryRequestOptions { PartitionKey = Prefix("acme") });
    results.Add(Print(Expect("organization query: prefix key and WHERE c.Organization", prefixAndWhere, 6, item => item.Organization == "acme")));

    var whereOnly = await QueryAsync<ProbeItem>(container, projectQuery, new QueryRequestOptions());
    results.Add(Print(Expect("organization query: WHERE c.Organization only", whereOnly, 6, item => item.Organization == "acme")));

    // Informational: does the prefix key in the options alone keep other organizations out? The platform will
    // always put c.Organization in the WHERE clause, whatever this shows.
    var prefixOnly = await QueryAsync<ProbeItem>(
        container,
        new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.PartitionKey, @project)").WithParameter("@project", "billing."),
        new QueryRequestOptions { PartitionKey = Prefix("acme") });
    var leaked = prefixOnly.Items.Count(item => item.Organization != "acme");
    results.Add(Print(prefixOnly.Error is not null
        ? CheckResult.Info("prefix key without WHERE c.Organization", prefixOnly.Charge, prefixOnly.Error)
        : CheckResult.Info("prefix key without WHERE c.Organization", prefixOnly.Charge, leaked == 0 ? $"{prefixOnly.Items.Count} items, all of acme" : $"{leaked} of {prefixOnly.Items.Count} items from other organizations")));

    // 5. GROUP BY and ORDER BY over an organization, as the counts per view and the ordered listings.
    var grouped = await QueryAsync<ViewCount>(
        container,
        new QueryDefinition("SELECT c.PartitionKey AS Partition, COUNT(1) AS Count FROM c WHERE c.Organization = @organization GROUP BY c.PartitionKey")
            .WithParameter("@organization", "acme"),
        new QueryRequestOptions { PartitionKey = Prefix("acme") });
    results.Add(Print(grouped.Error is not null
        ? CheckResult.Fail("GROUP BY over an organization", grouped.Charge, grouped.Error)
        : grouped.Items.Count == 3 && grouped.Items.All(row => row.Count == 3)
            ? CheckResult.Pass("GROUP BY over an organization", grouped.Charge, "3 partitions of 3 items")
            : CheckResult.Fail("GROUP BY over an organization", grouped.Charge, string.Join(", ", grouped.Items.Select(row => $"{row.Partition}={row.Count}")))));

    var ordered = await QueryAsync<ProbeItem>(
        container,
        new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization ORDER BY c.id").WithParameter("@organization", "acme"),
        new QueryRequestOptions { PartitionKey = Prefix("acme") });
    var sorted = ordered.Items.Select(item => item.Id).SequenceEqual(ordered.Items.Select(item => item.Id).Order(StringComparer.Ordinal));
    results.Add(Print(ordered.Error is not null
        ? CheckResult.Fail("ORDER BY over an organization", ordered.Charge, ordered.Error)
        : ordered.Items.Count == 9 && sorted && ordered.Items.All(item => item.Organization == "acme")
            ? CheckResult.Pass("ORDER BY over an organization", ordered.Charge, "9 items, sorted")
            : CheckResult.Fail("ORDER BY over an organization", ordered.Charge, $"{ordered.Items.Count} items, sorted: {sorted}")));

    // 6. Paging with continuation tokens, as every listing of the API.
    var paged = await QueryAsync<ProbeItem>(container, projectQuery, new QueryRequestOptions { PartitionKey = Prefix("acme"), MaxItemCount = 2 });
    results.Add(Print(paged.Error is not null
        ? CheckResult.Fail("paging with continuation tokens", paged.Charge, paged.Error)
        : paged.Items.Count == 6 && paged.Pages > 1
            ? CheckResult.Pass("paging with continuation tokens", paged.Charge, $"{paged.Pages} pages, 6 items")
            : CheckResult.Fail("paging with continuation tokens", paged.Charge, $"{paged.Pages} pages, {paged.Items.Count} items")));

    // 7. Transactional batch on the full key: the history item plus the catalog item of a key/value update.
    var batchKey = Key("acme", "billing.kv.default");
    var batchCatalog = await container.ReadItemAsync<ProbeItem>("kv.db.password", batchKey);
    using (var batch = await container.CreateTransactionalBatch(batchKey)
        .CreateItem(ProbeItem.Create("acme", "billing.kv.default", "kvv.db.password.1"))
        .ReplaceItem("kv.db.password", batchCatalog.Resource with { Version = 2 }, new TransactionalBatchItemRequestOptions { IfMatchEtag = batchCatalog.ETag })
        .ExecuteAsync())
    {
        results.Add(Print(batch.IsSuccessStatusCode
            ? CheckResult.Pass("transactional batch on the full key", batch.RequestCharge, $"{batch.Count} operations")
            : CheckResult.Fail("transactional batch on the full key", batch.RequestCharge, $"{(int)batch.StatusCode} {batch.ErrorMessage}")));
    }

    using (var failing = await container.CreateTransactionalBatch(batchKey)
        .CreateItem(ProbeItem.Create("acme", "billing.kv.default", "kv.batch.new"))
        .CreateItem(ProbeItem.Create("acme", "billing.kv.default", "kv.db.host"))
        .ExecuteAsync())
    {
        var rolledBack = false;

        try
        {
            await container.ReadItemAsync<ProbeItem>("kv.batch.new", batchKey);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            rolledBack = true;
        }

        results.Add(Print(!failing.IsSuccessStatusCode && rolledBack
            ? CheckResult.Pass("failing batch rolls back", failing.RequestCharge, $"{(int)failing.StatusCode}")
            : CheckResult.Fail("failing batch rolls back", failing.RequestCharge, $"status {(int)failing.StatusCode}, rolled back: {rolledBack}")));
    }

    // 8. Patch with an ETag condition, as the access and machine updates use.
    var patchKey = Key("globex", "portal.kv.default");
    var patchTarget = await container.ReadItemAsync<ProbeItem>("kv.db.host", patchKey);
    var patched = await container.PatchItemAsync<ProbeItem>(
        "kv.db.host",
        patchKey,
        [PatchOperation.Set("/Version", 7), PatchOperation.Remove("/Values/0")],
        new PatchItemRequestOptions { IfMatchEtag = patchTarget.ETag });
    results.Add(Print(patched.Resource.Version == 7 && patched.Resource.Values.Length == 1
        ? CheckResult.Pass("conditional patch (set, remove)", patched.RequestCharge)
        : CheckResult.Fail("conditional patch (set, remove)", patched.RequestCharge, $"version {patched.Resource.Version}, values {patched.Resource.Values.Length}")));

    // 9. ReadMany across organizations, as the dependency check of conditional reads.
    var many = await container.ReadManyItemsAsync<ProbeItem>(
    [
        ("kv.db.host", Key("acme", "billing.kv.prod")),
        ("kv.db.host", Key("globex", "billing.kv.prod")),
        ("kv.missing", Key("acme", "billing.kv.prod")),
    ]);
    results.Add(Print(many.Count == 2 && many.Select(item => item.Organization).Order().SequenceEqual(["acme", "globex"])
        ? CheckResult.Pass("ReadMany with full keys", many.RequestCharge, "2 of 3 found")
        : CheckResult.Fail("ReadMany with full keys", many.RequestCharge, $"{many.Count} found")));

    // 10. Stored procedure on the full key. The platform registers two but never calls them, so this is
    // informational: a failure here does not block the layout.
    try
    {
        await container.Scripts.CreateStoredProcedureAsync(new Microsoft.Azure.Cosmos.Scripts.StoredProcedureProperties
        {
            Id = "countInPartition",
            Body = """
                   function countInPartition() {
                       var collection = getContext().getCollection();
                       var accepted = collection.queryDocuments(collection.getSelfLink(), "SELECT VALUE COUNT(1) FROM c", function (err, items) {
                           if (err) throw err;
                           getContext().getResponse().setBody(items[0]);
                       });
                       if (!accepted) throw new Error("not accepted");
                   }
                   """,
        });
        var executed = await container.Scripts.ExecuteStoredProcedureAsync<int>("countInPartition", Key("acme", "portal.kv.default"), []);
        results.Add(Print(CheckResult.Info("stored procedure on the full key", executed.RequestCharge, $"ran, counted {executed.Resource}")));
    }
    catch (CosmosException exception)
    {
        results.Add(Print(CheckResult.Info("stored procedure on the full key", exception.RequestCharge, $"not supported here: {(int)exception.StatusCode}")));
    }

    // 11. TTL of a single item, as versions and deleted keys expire. Emulators may expire lazily.
    await container.CreateItemAsync(ProbeItem.Create("acme", "billing.kv.default", "kvv.expiring") with { Ttl = 2 }, batchKey);
    await Task.Delay(TimeSpan.FromSeconds(6));
    try
    {
        await container.ReadItemAsync<ProbeItem>("kvv.expiring", batchKey);
        results.Add(Print(CheckResult.Info("item with ttl 2 s is gone after 6 s", 0, "still there (lazy expiry)")));
    }
    catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
    {
        results.Add(Print(CheckResult.Pass("item with ttl 2 s is gone after 6 s", exception.RequestCharge)));
    }

    // 12. Deleting one organization's items: no delete by a key prefix, so query the ids and delete per partition.
    var stopwatch = Stopwatch.StartNew();
    var toDelete = await QueryAsync<ProbeItem>(
        container,
        new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization").WithParameter("@organization", "globex"),
        new QueryRequestOptions { PartitionKey = Prefix("globex") });
    var deleteCharge = toDelete.Charge;

    foreach (var group in toDelete.Items.GroupBy(item => item.PartitionKey))
    {
        var deletion = container.CreateTransactionalBatch(Key("globex", group.Key));

        foreach (var item in group)
        {
            deletion.DeleteItem(item.Id);
        }

        using var deleted = await deletion.ExecuteAsync();
        deleteCharge += deleted.RequestCharge;
    }

    var remaining = await QueryAsync<ProbeItem>(
        container,
        new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization").WithParameter("@organization", "globex"),
        new QueryRequestOptions { PartitionKey = Prefix("globex") });
    var acmeLeft = await QueryAsync<ProbeItem>(
        container,
        new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization").WithParameter("@organization", "acme"),
        new QueryRequestOptions { PartitionKey = Prefix("acme") });
    results.Add(Print(remaining.Items.Count == 0 && acmeLeft.Items.Count > 0
        ? CheckResult.Pass("delete one organization (query + batches)", deleteCharge, $"{toDelete.Items.Count} items in {stopwatch.ElapsedMilliseconds} ms, acme untouched")
        : CheckResult.Fail("delete one organization (query + batches)", deleteCharge, $"{remaining.Items.Count} left, acme {acmeLeft.Items.Count}")));
}

// Informational: the listings that ContainerFactory (organization containers) and the test cleanup (databases) use
// today. The shared container no longer needs the first one.
static async Task ListResourcesAsync(CosmosClient client, Database database, List<CheckResult> results)
{
    try
    {
        var names = new List<string>();
        using var iterator = database.GetContainerQueryIterator<ContainerProperties>();

        while (iterator.HasMoreResults)
        {
            names.AddRange((await iterator.ReadNextAsync()).Select(properties => properties.Id));
        }

        results.Add(Print(CheckResult.Info("list the containers of a database", 0, string.Join(", ", names.Order()))));
    }
    catch (Exception exception)
    {
        results.Add(Print(CheckResult.Info("list the containers of a database", 0, $"not supported here: {FirstLine(exception.Message)}")));
    }

    try
    {
        var count = 0;
        using var iterator = client.GetDatabaseQueryIterator<DatabaseProperties>();

        while (iterator.HasMoreResults)
        {
            count += (await iterator.ReadNextAsync()).Count;
        }

        results.Add(Print(CheckResult.Info("list the databases of the account", 0, $"{count} database(s)")));
    }
    catch (Exception exception)
    {
        results.Add(Print(CheckResult.Info("list the databases of the account", 0, $"not supported here: {FirstLine(exception.Message)}")));
    }
}

static PartitionKey Key(string organization, string partition)
{
    return new PartitionKeyBuilder().Add(organization).Add(partition).Build();
}

static PartitionKey Prefix(string organization)
{
    return new PartitionKeyBuilder().Add(organization).Build();
}

static async Task<QueryResult<T>> QueryAsync<T>(Container container, QueryDefinition query, QueryRequestOptions requestOptions)
{
    var items = new List<T>();
    double charge = 0;
    var pages = 0;

    try
    {
        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            charge += page.RequestCharge;
            items.AddRange(page);
            pages++;
        }

        return new QueryResult<T>(items, charge, pages, null);
    }
    catch (CosmosException exception)
    {
        return new QueryResult<T>(items, charge + exception.RequestCharge, pages, $"{(int)exception.StatusCode} {FirstLine(exception.Message)}");
    }
}

static CheckResult Expect(string name, QueryResult<ProbeItem> result, int count, Func<ProbeItem, bool> belongs)
{
    if (result.Error is not null)
    {
        return CheckResult.Fail(name, result.Charge, result.Error);
    }

    var foreign = result.Items.Count(item => !belongs(item));
    return result.Items.Count == count && foreign == 0
        ? CheckResult.Pass(name, result.Charge, $"{count} items")
        : CheckResult.Fail(name, result.Charge, $"{result.Items.Count} items, {foreign} not expected (wanted {count})");
}

static CheckResult Print(CheckResult result)
{
    var color = result.Outcome switch { "PASS" => ConsoleColor.Green, "FAIL" => ConsoleColor.Red, _ => ConsoleColor.Yellow };
    var previous = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write($"{result.Outcome,-5}");
    Console.ForegroundColor = previous;
    Console.WriteLine($" {result.Name,-55} {result.Charge,8:0.00} RU  {result.Detail}");
    return result;
}

static string FirstLine(string text)
{
    // The service's reason is the useful part of a CosmosException message: "... Reason: ({ "Errors": [...] })".
    var reason = text.IndexOf("Reason:", StringComparison.Ordinal);
    var line = string.Join(' ', (reason >= 0 ? text[reason..] : text).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    return line.Length > 300 ? line[..300] + "…" : line;
}

internal sealed record ProbeOptions(string Endpoint, string? Key, string Throughput, bool Insecure, bool Keep, string Label)
{
    public static ProbeOptions? Parse(string[] args)
    {
        string endpoint = "https://localhost:8081/";
        string? key = null;
        var throughput = "autoscale";
        bool? insecure = null;
        var keep = false;
        string? label = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--endpoint" when i + 1 < args.Length: endpoint = args[++i]; break;
                case "--key" when i + 1 < args.Length: key = args[++i]; break;
                case "--throughput" when i + 1 < args.Length && args[i + 1] is "autoscale" or "manual" or "none": throughput = args[++i]; break;
                case "--insecure": insecure = true; break;
                case "--keep": keep = true; break;
                case "--label" when i + 1 < args.Length: label = args[++i]; break;
                default: return null;
            }
        }

        var host = new Uri(endpoint).Host;
        var isLocal = host is "localhost" or "127.0.0.1";
        return new ProbeOptions(endpoint, key, throughput, insecure ?? isLocal, keep, label ?? endpoint);
    }
}

internal sealed record CheckResult(string Outcome, string Name, double Charge, string Detail)
{
    public static CheckResult Pass(string name, double charge, string detail = "") => new("PASS", name, charge, detail);

    public static CheckResult Fail(string name, double charge, string detail) => new("FAIL", name, charge, detail);

    public static CheckResult Info(string name, double charge, string detail) => new("INFO", name, charge, detail);
}

internal sealed record QueryResult<T>(List<T> Items, double Charge, int Pages, string? Error);

internal sealed record ViewCount(string Partition, int Count);

internal sealed record ProbeItem
{
    [JsonProperty("id")]
    public required string Id { get; init; }

    public required string Organization { get; init; }

    public required string PartitionKey { get; init; }

    public int Version { get; init; } = 1;

    public string[] Values { get; init; } = [];

    [JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]
    public int? Ttl { get; init; }

    public static ProbeItem Create(string organization, string partition, string id)
    {
        return new ProbeItem { Id = id, Organization = organization, PartitionKey = partition, Values = ["a", "b"] };
    }
}
