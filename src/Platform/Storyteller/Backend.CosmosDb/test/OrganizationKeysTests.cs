#nullable enable

using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The hierarchical keys of the container organizations and the scope of one organization, without Cosmos DB.
public class OrganizationKeysTests
{
    [Fact]
    public void Key_HasTheOrganizationThenThePartition()
    {
        OrganizationKeys.Key("acme", "billing.kv.default").ToString().Should().Be("[\"acme\",\"billing.kv.default\"]");
    }

    [Fact]
    public void Prefix_HasTheOrganizationOnly()
    {
        OrganizationKeys.Prefix("acme").ToString().Should().Be("[\"acme\"]");
    }

    [Fact]
    public void Keys_OfTwoOrganizations_DifferForTheSamePartition()
    {
        OrganizationKeys.Key("acme", "billing.kv.default").Should().NotBe(OrganizationKeys.Key("globex", "billing.kv.default"));
    }

    [Fact]
    public void QueryOptions_WithoutAPartition_UseThePrefix()
    {
        var scope = new OrganizationContainer("acme", Mock.Of<Container>());

        var options = scope.QueryOptions(maxItemCount: 25);

        options.PartitionKey.Should().Be(OrganizationKeys.Prefix("acme"));
        options.MaxItemCount.Should().Be(25);
    }

    [Fact]
    public void QueryOptions_WithAPartition_UseTheFullKey()
    {
        var scope = new OrganizationContainer("acme", Mock.Of<Container>());

        scope.QueryOptions("billing.kv.default").PartitionKey.Should().Be(OrganizationKeys.Key("acme", "billing.kv.default"));
        scope.QueryOptions("billing.kv.default").MaxItemCount.Should().BeNull();
    }

    [Fact]
    public void WithOrganization_AddsTheParameter()
    {
        var scope = new OrganizationContainer("acme", Mock.Of<Container>());

        var query = scope.WithOrganization(new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization"));

        query.GetQueryParameters().Should().ContainSingle()
            .Which.Should().Be((OrganizationContainer.OrganizationParameter, (object)"acme"));
    }

    [Fact]
    public void Scope_NeedsAnOrganization()
    {
        var create = () => new OrganizationContainer(" ", Mock.Of<Container>());

        create.Should().Throw<System.ArgumentException>();
    }
}
