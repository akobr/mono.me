using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// An access audit record as Cosmos DB stores it, read straight from its container: the entity, and the document, to look
// for what a record must never hold.
public sealed record StoredAccessAudit(AccessAuditEntity Record, JsonObject Document)
{
    // The retention of the fixture, which sets no AccessAudit section: two years.
    public static readonly int DefaultTimeToLiveInSeconds = (int)TimeSpan.FromDays(new AccessAuditOptions().RetentionDays).TotalSeconds;

    // Properties of a credential, its hash, a certificate or a key, under any name a record could carry them.
    private static readonly string[] SecretProperties =
    [
        "AccessKey",
        "HashedSecret",
        "Certificate",
        "CertificatePassword",
        "Pkcs12",
        "Password",
        "Secret",
        "Jwks",
        "PrivateKeyPkcs8",
    ];

    // The records of a partition of the container organizations ("{project}.access", "{project}.federation"), oldest first.
    public static async Task<IReadOnlyList<StoredAccessAudit>> ReadAsync(IServiceProvider services, string organization, string partitionKey)
    {
        var repository = services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(organization);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization AND STARTSWITH(c.id, @prefix)")
            .WithParameter("@organization", organization)
            .WithParameter("@prefix", AccessAuditEntity.IdPrefix);

        return await QueryAsync(services, repository.Container, query, repository.QueryOptions(partitionKey));
    }

    // The records of an access point (organization.project) in core "access", oldest first.
    public static async Task<IReadOnlyList<StoredAccessAudit>> ReadCoreAsync(IServiceProvider services, string accessPointKey)
    {
        var core = services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.AccessPointKey = @key")
            .WithParameter("@prefix", AccessAuditEntity.IdPrefix)
            .WithParameter("@key", accessPointKey);

        return await QueryAsync(services, core, query, new QueryRequestOptions { PartitionKey = new PartitionKey(AccessPointEntity.Partition) });
    }

    // The machine's records of a project, oldest first.
    public static async Task<IReadOnlyList<StoredAccessAudit>> ReadMachineAsync(IServiceProvider services, string organization, string project, string machineId)
    {
        return (await ReadAsync(services, organization, $"{project}.access"))
            .Where(stored => stored.Record.Target?.Kind == AccessAuditTargetKind.Machine && stored.Record.Target.Id == machineId)
            .ToList();
    }

    // Neither the record nor a JSON value of it has a property of a secret's name, and its document holds none of the
    // given secrets (keys, hashes, certificates, passwords) and no stored mask (it keeps a client secret's first characters).
    public void ShouldHoldNoSecret(params string?[] secrets)
    {
        var text = Document.ToJsonString();

        // the serialized text escapes some characters (a + or a / of a key), so the decoded strings are checked too,
        // also inside the JSON of the old and new values
        var strings = StringValues(Document)
            .Concat(new[] { Record.OldValue, Record.NewValue }.Where(IsJson).SelectMany(value => StringValues(JsonNode.Parse(value!))))
            .ToList();

        foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            text.ShouldNotContain(secret!, Case.Sensitive);
            strings.ShouldNotContain(value => value.Contains(secret!, StringComparison.Ordinal), $"a stored string holds the secret {secret}");
        }

        text.ShouldNotContain("***");
        PropertyNames(Document).Intersect(SecretProperties, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();

        foreach (var value in new[] { Record.OldValue, Record.NewValue })
        {
            if (value is not null && (value.StartsWith('{') || value.StartsWith('[')))
            {
                PropertyNames(JsonNode.Parse(value)).Intersect(SecretProperties, StringComparer.OrdinalIgnoreCase).ShouldBeEmpty();
            }
        }
    }

    // The record of a change by the caller, kept for the default retention, in the partition of the change.
    public void ShouldBeBy(AccessCaller caller, string organization, string project, string partitionKey)
    {
        Record.Actor.ShouldBe(caller.Actor);
        Record.CorrelationId.ShouldBe(caller.CorrelationId);
        Record.Organization.ShouldBe(organization);
        Record.Project.ShouldBe(project);
        Record.AccessPointKey.ShouldBe($"{organization}.{project}");
        Record.PartitionKey.ShouldBe(partitionKey);
        Record.TimeToLiveInSeconds.ShouldBe(DefaultTimeToLiveInSeconds);
        Record.Id.ShouldStartWith(AccessAuditEntity.IdPrefix);
    }

    // A JSON value of the record (OldValue, NewValue) as an object.
    public static JsonObject Json(string? value)
    {
        return JsonNode.Parse(value.ShouldNotBeNull())!.AsObject();
    }

    private static async Task<IReadOnlyList<StoredAccessAudit>> QueryAsync(IServiceProvider services, Container container, QueryDefinition query, QueryRequestOptions options)
    {
        var serializerOptions = services.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
        var records = new List<StoredAccessAudit>();
        using var iterator = container.GetItemQueryStreamIterator(query, requestOptions: options);

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            response.EnsureSuccessStatusCode();
            var page = (await JsonNode.ParseAsync(response.Content))!.AsObject();

            foreach (var document in page["Documents"]!.AsArray().Select(node => node!.AsObject()))
            {
                records.Add(new StoredAccessAudit(document.Deserialize<AccessAuditEntity>(serializerOptions)!, document));
            }
        }

        // The id order is the time order.
        return records.OrderBy(stored => stored.Record.Id, StringComparer.Ordinal).ToList();
    }

    // Every string value of the JSON, decoded, at any depth.
    private static IEnumerable<string> StringValues(JsonNode? node)
    {
        return node switch
        {
            JsonObject json => json.SelectMany(property => StringValues(property.Value)),
            JsonArray array => array.SelectMany(StringValues),
            JsonValue value when value.TryGetValue<string>(out var text) => [text],
            _ => [],
        };
    }

    private static bool IsJson(string? value)
    {
        return value is not null && (value.StartsWith('{') || value.StartsWith('['));
    }

    private static IEnumerable<string> PropertyNames(JsonNode? node)
    {
        return node switch
        {
            JsonObject json => json.SelectMany(property => PropertyNames(property.Value).Prepend(property.Key)),
            JsonArray array => array.SelectMany(PropertyNames),
            _ => [],
        };
    }
}
