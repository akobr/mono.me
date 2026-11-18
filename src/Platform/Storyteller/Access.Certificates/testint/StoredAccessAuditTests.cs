using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Entities.Access;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// The helper itself, without Cosmos DB: a secret is found also where the serialized text escapes it.
public class StoredAccessAuditTests
{
    private const string Secret = "2s.key+with/escapes";

    [Fact]
    public void ShouldHoldNoSecret_FindsASecretTheSerializedTextEscapes()
    {
        // "+" is written as + by the default encoder, so a search of the text alone misses it
        var stored = Stored(new JsonObject { ["Detail"] = $"The key {Secret} was replaced." });

        stored.Document.ToJsonString().ShouldNotContain(Secret);
        Should.Throw<ShouldAssertException>(() => stored.ShouldHoldNoSecret(Secret));
    }

    [Fact]
    public void ShouldHoldNoSecret_FindsASecretInTheJsonOfAValue()
    {
        var stored = Stored(new JsonObject(), newValue: $"{{\"Note\":\"{Secret}\"}}");

        Should.Throw<ShouldAssertException>(() => stored.ShouldHoldNoSecret(Secret));
    }

    [Fact]
    public void ShouldHoldNoSecret_PassesARecordWithoutTheSecret()
    {
        Stored(new JsonObject { ["Detail"] = "The API key was replaced." }, newValue: "{\"CredentialKind\":\"ApiKey\"}")
            .ShouldHoldNoSecret(Secret);
    }

    private static StoredAccessAudit Stored(JsonObject document, string? newValue = null)
    {
        var record = new AccessAuditEntity
        {
            PartitionKey = "p.access",
            Organization = "o",
            Id = AccessAuditEntity.NewId(DateTimeOffset.UtcNow),
            TimeToLiveInSeconds = 60,
            AccessPointKey = "o.p",
            Time = "2026-11-18T00:00:00.0000000Z",
            Action = AccessAuditAction.MachineReset,
            Actor = AuditActor.System,
            NewValue = newValue,
            Detail = document["Detail"]?.GetValue<string>(),
        };
        return new StoredAccessAudit(record, document);
    }
}
