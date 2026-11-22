#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// AuditActor.Via and ViaClient through the serializers of the Cosmos DB layer (EntryPoint): System.Text.Json for
// annotations and views, Newtonsoft for configurations and key/values. Each is written only when set, and documents
// written before it existed still read. No database: outside the CosmosDb collection.
public class AuditActorSerializationTests
{
    private const string ClientId = "https://claude.ai/oauth/mcp-oauth-client-metadata";

    private static readonly AuditActor ThroughMcp = AuditActor.Account("user-1", "Ada") with { Via = "mcp" };
    private static readonly AuditActor ThroughMcpAndAClient = ThroughMcp with { ViaClient = ClientId };
    private static readonly AuditActor Direct = AuditActor.Account("user-1", "Ada");

    private readonly JsonSerializerOptions _systemOptions;
    private readonly JsonSerializerSettings _newtonsoftSettings;

    public AuditActorSerializationTests()
    {
        var services = new ServiceCollection()
            .AddCosmosDbAnnotations(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build())
            .BuildServiceProvider();
        _systemOptions = services.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
        _newtonsoftSettings = services.GetRequiredService<IOptions<JsonSerializerSettings>>().Value;
    }

    [Fact]
    public void SystemTextJson_ActorWithVia_RoundTrips()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ThroughMcp, _systemOptions);

        JsonNode.Parse(json)!["Via"]!.GetValue<string>().Should().Be("mcp");
        System.Text.Json.JsonSerializer.Deserialize<AuditActor>(json, _systemOptions).Should().Be(ThroughMcp);
    }

    [Fact]
    public void SystemTextJson_ActorWithoutVia_LeavesItOut()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Direct, _systemOptions);

        JsonNode.Parse(json)!.AsObject().ContainsKey("Via").Should().BeFalse();
        JsonNode.Parse(json)!.AsObject().ContainsKey("ViaClient").Should().BeFalse();
        System.Text.Json.JsonSerializer.Deserialize<AuditActor>(json, _systemOptions).Should().Be(Direct);
    }

    [Fact]
    public void SystemTextJson_ActorWithAClient_RoundTrips()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ThroughMcpAndAClient, _systemOptions);

        JsonNode.Parse(json)!["ViaClient"]!.GetValue<string>().Should().Be(ClientId);
        System.Text.Json.JsonSerializer.Deserialize<AuditActor>(json, _systemOptions).Should().Be(ThroughMcpAndAClient);
    }

    [Fact]
    public void SystemTextJson_ActorWithoutAClient_LeavesItOut()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ThroughMcp, _systemOptions);

        JsonNode.Parse(json)!.AsObject().ContainsKey("ViaClient").Should().BeFalse();
        System.Text.Json.JsonSerializer.Deserialize<AuditActor>(json, _systemOptions).Should().Be(ThroughMcp);
    }

    [Fact]
    public void SystemTextJson_DocumentWrittenBeforeViaClient_Reads()
    {
        var stamp = System.Text.Json.JsonSerializer.Deserialize<AuditStamp>(
            """{ "At": "2026-11-01T12:00:00+00:00", "By": { "Kind": "Account", "Id": "user-1", "Name": "Ada", "Via": "mcp" } }""",
            _systemOptions);

        stamp!.By.Should().Be(ThroughMcp);
        stamp.By.ViaClient.Should().BeNull();
    }

    [Fact]
    public void SystemTextJson_DocumentWrittenBeforeVia_Reads()
    {
        var stamp = System.Text.Json.JsonSerializer.Deserialize<AuditStamp>(
            """{ "At": "2026-10-01T12:00:00+00:00", "By": { "Kind": "Account", "Id": "user-1", "Name": "Ada" } }""",
            _systemOptions);

        stamp!.By.Should().Be(Direct);
        stamp.By.Via.Should().BeNull();
    }

    [Fact]
    public void Newtonsoft_ActorWithVia_RoundTrips()
    {
        var json = JsonConvert.SerializeObject(AuditStamp.Of(ThroughMcp, System.DateTimeOffset.UnixEpoch), _newtonsoftSettings);

        JObject.Parse(json)["By"]!["Via"]!.Value<string>().Should().Be("mcp");
        JsonConvert.DeserializeObject<AuditStamp>(json, _newtonsoftSettings)!.By.Should().Be(ThroughMcp);
    }

    [Fact]
    public void Newtonsoft_ActorWithoutVia_LeavesItOut()
    {
        var json = JsonConvert.SerializeObject(Direct, _newtonsoftSettings);

        JObject.Parse(json).ContainsKey("Via").Should().BeFalse();
        JObject.Parse(json).ContainsKey("ViaClient").Should().BeFalse();
        JsonConvert.DeserializeObject<AuditActor>(json, _newtonsoftSettings).Should().Be(Direct);
    }

    [Fact]
    public void Newtonsoft_ActorWithAClient_RoundTrips()
    {
        var json = JsonConvert.SerializeObject(AuditStamp.Of(ThroughMcpAndAClient, System.DateTimeOffset.UnixEpoch), _newtonsoftSettings);

        JObject.Parse(json)["By"]!["ViaClient"]!.Value<string>().Should().Be(ClientId);
        JsonConvert.DeserializeObject<AuditStamp>(json, _newtonsoftSettings)!.By.Should().Be(ThroughMcpAndAClient);
    }

    [Fact]
    public void Newtonsoft_ActorWithoutAClient_LeavesItOut()
    {
        var json = JsonConvert.SerializeObject(ThroughMcp, _newtonsoftSettings);

        JObject.Parse(json).ContainsKey("ViaClient").Should().BeFalse();
        JsonConvert.DeserializeObject<AuditActor>(json, _newtonsoftSettings).Should().Be(ThroughMcp);
    }

    [Fact]
    public void Newtonsoft_DocumentWrittenBeforeViaClient_Reads()
    {
        var actor = JsonConvert.DeserializeObject<AuditActor>("""{ "Kind": "Account", "Id": "user-1", "Name": "Ada", "Via": "mcp" }""", _newtonsoftSettings);

        actor.Should().Be(ThroughMcp);
        actor!.ViaClient.Should().BeNull();
    }

    [Fact]
    public void Newtonsoft_DocumentWrittenBeforeVia_Reads()
    {
        var actor = JsonConvert.DeserializeObject<AuditActor>("""{ "Kind": "Account", "Id": "user-1", "Name": "Ada" }""", _newtonsoftSettings);

        actor.Should().Be(Direct);
        actor!.Via.Should().BeNull();
    }
}
