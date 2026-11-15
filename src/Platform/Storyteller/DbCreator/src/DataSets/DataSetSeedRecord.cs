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

    // What became of project-settings.json; null for a set without one (and in a record of a seed before the project
    // settings).
    public ViewImportOutcome? ProjectSettings { get; init; }

    // The document files, the annotations of annotations.jsonl and the pairs of key-values.json the seed read, and the
    // outcomes of all of them and of the project settings together. A record written before sets had pairs has no
    // KeyValues (0).
    public int Documents { get; init; }

    public int Annotations { get; init; }

    public int KeyValues { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Unchanged { get; init; }

    public int Skipped { get; init; }

    public int Warnings { get; init; }

    public static string GetId(string view, string set) => $"{IdPrefix}{view}.{set}";

    public static string GetPartitionKey(string project) => ViewEntity.GetPartitionKey(project);
}
