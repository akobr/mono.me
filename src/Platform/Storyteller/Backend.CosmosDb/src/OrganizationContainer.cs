using System;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

// The data of one organization in the shared container "organizations", partitioned by /Organization and then by the
// PartitionKey the items have always had. A thin scope: the call sites keep their SDK calls and take every partition key
// and every organization-wide query option from here, so no caller builds a partition key of organization data itself.
public sealed class OrganizationContainer : IContainerRepository
{
    // The parameter of the predicate "c.Organization = @organization" every organization-wide query writes out.
    public const string OrganizationParameter = "@organization";

    public OrganizationContainer(string organization, Container container)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            throw new ArgumentException("An organization container needs the name of its organization.", nameof(organization));
        }

        Organization = organization;
        Container = container;
        Prefix = OrganizationKeys.Prefix(organization);
    }

    public string Organization { get; }

    public Container Container { get; }

    // (organization): the key prefix of an organization-wide query. It routes the query to the physical partitions of the
    // organization; isolation is the predicate on c.Organization, never the prefix alone.
    public PartitionKey Prefix { get; }

    // (organization, partition): every point operation, single-partition query, ReadMany item and transactional batch.
    public PartitionKey Key(string partitionKey)
    {
        return OrganizationKeys.Key(Organization, partitionKey);
    }

    // The options of a query in one partition, or of an organization-wide query (the prefix) when partitionKey is null.
    public QueryRequestOptions QueryOptions(string? partitionKey = null, int? maxItemCount = null)
    {
        var options = new QueryRequestOptions { PartitionKey = partitionKey is null ? Prefix : Key(partitionKey) };

        if (maxItemCount is not null)
        {
            options.MaxItemCount = maxItemCount;
        }

        return options;
    }

    // Adds the organization as @organization to a query whose text has "c.Organization = @organization".
    public QueryDefinition WithOrganization(QueryDefinition query)
    {
        return query.WithParameter(OrganizationParameter, Organization);
    }
}
