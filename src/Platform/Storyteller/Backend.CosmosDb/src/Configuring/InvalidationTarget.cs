using System;
using System.Linq.Expressions;
using _42.Platform.Storyteller.Entities.Configurations;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

// The configuration items of one annotation type whose cached calculation an invalidation clears: the predicate
// selects items of that type only, in one partition (the full hierarchical key, FullKey.GetCosmosPartitionKey), or
// across the partitions of the organization when the partition key is null.
internal sealed record InvalidationTarget(
    string TypeCode,
    Expression<Func<ConfigurationEntity, bool>> Predicate,
    PartitionKey? PartitionKey = null);
