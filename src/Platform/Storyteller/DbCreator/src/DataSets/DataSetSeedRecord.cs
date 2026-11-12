using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Entities.Annotations;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The record of a set seeded without failures, in the container organizations next to the registered views
// (partition "{project}.meta", id "seed.{view}.{set}"). It has no ViewName, ProjectName or AnnotationType and its id
// does not start with "view.", so view discovery, the registry and the queries of annotations never see it.
public sealed record class DataSetSeedRecord
{
    public const string IdPrefix = "seed.";

    // The organization of the item: the first level of the hierarchical partition key of the container organizations.
    public required string Organization { get; init; }

    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string Id => GetId(View, Set);

    public required string Set { get; init; }

    public required string View { get; init; }

    public required int Version { get; init; }

    // DataSetFolder.ComputeHash
    public required string Hash { get; init; }

    public required AuditStamp Seeded { get; init; }

    // The document files and the annotations of annotations.jsonl the seed read, and the outcomes of both together.
    public int Documents { get; init; }

    public int Annotations { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Unchanged { get; init; }

    public int Skipped { get; init; }

    public int Warnings { get; init; }

    public static string GetId(string view, string set) => $"{IdPrefix}{view}.{set}";

    public static string GetPartitionKey(string project) => ViewEntity.GetPartitionKey(project);
}
