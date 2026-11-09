#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The guard of the suite (OrganizationQueryGuard) refuses an organization-wide query without the predicate on
// c.Organization, written in SQL or by LINQ, and lets through the queries of one full partition.
public class OrganizationQueryGuardTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = "guard-tests";

    private OrganizationContainer Repository => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Organization);

    [Fact]
    public async Task SqlQuery_WithThePrefixAndWithoutThePredicate_IsRefused()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.PartitionKey = @partition")
            .WithParameter("@partition", "p");

        var act = () => ReadAllAsync(Repository.Container.GetItemQueryIterator<Row>(query, requestOptions: Repository.QueryOptions()));

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("without the predicate");
    }

    [Fact]
    public async Task SqlQuery_WithThePrefixAndThePredicate_Runs()
    {
        var query = Repository.WithOrganization(new QueryDefinition(
            $"SELECT * FROM c WHERE c.Organization = {OrganizationContainer.OrganizationParameter} AND c.PartitionKey = @partition")
            .WithParameter("@partition", "p"));

        var rows = await ReadAllAsync(Repository.Container.GetItemQueryIterator<Row>(query, requestOptions: Repository.QueryOptions()));

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task SqlQuery_InOneFullPartitionWithoutThePredicate_Runs()
    {
        var query = new QueryDefinition("SELECT * FROM c");

        var rows = await ReadAllAsync(Repository.Container.GetItemQueryIterator<Row>(query, requestOptions: Repository.QueryOptions("p")));

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task LinqQuery_WithThePrefixAndWithoutThePredicate_IsRefused()
    {
        var iterator = Repository.Container
            .GetItemLinqQueryable<Row>(requestOptions: Repository.QueryOptions())
            .Where(row => row.PartitionKey == "p")
            .ToFeedIterator();

        var act = () => ReadAllAsync(iterator);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("without the predicate");
    }

    [Fact]
    public async Task LinqQuery_WithThePrefixAndThePredicate_Runs()
    {
        var organization = Repository.Organization;
        var iterator = Repository.Container
            .GetItemLinqQueryable<Row>(requestOptions: Repository.QueryOptions())
            .Where(row => row.Organization == organization && row.PartitionKey == "p")
            .ToFeedIterator();

        var rows = await ReadAllAsync(iterator);

        rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.Organization = @organization", "acme", true)]
    [InlineData("SELECT * FROM c WHERE c.Organization=@organization AND c.Deleted = false", "acme", true)]
    [InlineData("SELECT * FROM c WHERE c.Organization = @organization", null, false)]
    [InlineData("SELECT * FROM c WHERE c.Organization = @organizations", "acme", false)]
    [InlineData("SELECT VALUE root FROM root WHERE ((root[\"Organization\"] = \"acme\") AND (root[\"PartitionKey\"] = \"p\"))", null, true)]
    [InlineData("SELECT VALUE root FROM root WHERE (root[\"PartitionKey\"] = \"p\")", null, false)]
    [InlineData("SELECT * FROM c WHERE c.PartitionKey = @partition", "acme", false)]
    [InlineData("SELECT * FROM c WHERE ( ( c.id = @param_id0 AND c[\"Organization\"] = @param_pk00 AND c[\"PartitionKey\"] = @param_pk01 ) OR ( c.id = @param_id1 AND c[\"Organization\"] = @param_pk10 AND c[\"PartitionKey\"] = @param_pk11 ) )", null, true)]
    [InlineData("SELECT * FROM c WHERE ( ( c.id = @param_id0 AND c[\"Organization\"] = @param_pk00 ) OR ( c.id = @param_id1 ) )", null, false)]
    public void FiltersOnOrganization_RecognizesThePredicate(string text, string? parameter, bool expected)
    {
        OrganizationQueryGuard.FiltersOnOrganization(text, parameter).Should().Be(expected);
    }

    private static async Task<List<T>> ReadAllAsync<T>(FeedIterator<T> iterator)
    {
        using (iterator)
        {
            var rows = new List<T>();
            while (iterator.HasMoreResults)
            {
                rows.AddRange(await iterator.ReadNextAsync());
            }

            return rows;
        }
    }

    private sealed record Row
    {
        public string Organization { get; init; } = string.Empty;

        public string PartitionKey { get; init; } = string.Empty;
    }
}
