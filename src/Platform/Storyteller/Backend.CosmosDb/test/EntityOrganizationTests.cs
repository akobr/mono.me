#nullable enable

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Entities;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Every item type of the container organizations carries Organization, the first level of its partition key, under that
// exact name in both serializers. Cosmos DB refuses a write whose document misses it; this finds the type first.
public class EntityOrganizationTests
{
    // Items of the core container (single path /PartitionKey): no organization.
    private static readonly string[] CoreTypes =
    [
        "AccessPointEntity",
        "AccountEntity",
        "InvitationEntity",
        "CertificateAuthorityEntity",
        "TokenSigningKeyEntity",
    ];

    public static TheoryData<string> OrganizationEntityTypes()
    {
        var data = new TheoryData<string>();

        foreach (var type in StoredTypes().Where(type => !CoreTypes.Contains(type.Name)))
        {
            data.Add(type.FullName!);
        }

        return data;
    }

    [Fact]
    public void TheEntityTypes_AreFound()
    {
        StoredTypes().Select(type => type.Name).Should().Contain(["SubjectEntity", "KeyValueEntity", "MachineAccessEntity", "ViewTransferEntity", "AccessPointEntity"]);
    }

    [Theory]
    [MemberData(nameof(OrganizationEntityTypes))]
    public void AnOrganizationEntity_SerializesItsOrganization(string typeName)
    {
        var type = typeof(Entity).Assembly.GetType(typeName)!;
        var property = type.GetProperty("Organization", BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull($"{type.Name} is stored in the container organizations");
        property!.PropertyType.Should().Be<string>();
        property.GetCustomAttribute<RequiredMemberAttribute>().Should().NotBeNull($"{type.Name}.Organization must be required");

        var instance = RuntimeHelpers.GetUninitializedObject(type);
        property.SetValue(instance, "acme");

        System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(instance, type, Json.JsonSerializationOptions.SystemOptions))!
            ["Organization"]!.GetValue<string>().Should().Be("acme");
        JObject.FromObject(instance, Newtonsoft.Json.JsonSerializer.Create(Json.JsonSerializationOptions.NewtonsoftOptions))
            .Value<string>("Organization").Should().Be("acme");
    }

    // The concrete records of Entities that have a partition key: what the backend stores.
    private static Type[] StoredTypes()
    {
        return typeof(Entity).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && type.Namespace?.StartsWith("_42.Platform.Storyteller.Entities", StringComparison.Ordinal) == true
                           && type.GetProperty("PartitionKey") is not null
                           && type.Name.EndsWith("Entity", StringComparison.Ordinal))
            .ToArray();
    }
}
