using System;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

// The hierarchical partition keys of the container "organizations" (/Organization, /PartitionKey).
public static class OrganizationKeys
{
    // The paths of the key, in their order; the property names of the items.
    public const string OrganizationPath = "/Organization";
    public const string PartitionKeyPath = "/PartitionKey";

    // Both levels: the logical partition of one organization.
    public static PartitionKey Key(string organization, string partitionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentNullException.ThrowIfNull(partitionKey);
        return new PartitionKeyBuilder().Add(organization).Add(partitionKey).Build();
    }

    // The first level: the prefix of an organization-wide query.
    public static PartitionKey Prefix(string organization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        return new PartitionKeyBuilder().Add(organization).Build();
    }
}
