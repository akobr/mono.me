using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// A configuration of a view without its documents, for listings.
public record class ConfigurationSummary
{
    public required string AnnotationKey { get; init; }

    public required AnnotationType AnnotationType { get; init; }

    public required ulong Version { get; init; }

    public string? Author { get; init; }

    // Hash of the calculated (effective) document; null until it is calculated.
    public string? Hash { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    // The annotation's own stored document is a non-empty object.
    public bool HasContent { get; init; }
}

public record class ConfigurationsResponse
{
    public required IReadOnlyList<ConfigurationSummary> Configurations { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationSchemaKind
{
    Type = 0,
    Annotation,
    DescendantType,
}

// A schema document of a view without its content, for listings.
public record class ConfigurationSchemaSummary
{
    public required ConfigurationSchemaKind Kind { get; init; }

    // Annotation type code (rst, sbt, …) for Type and DescendantType.
    public string? AnnotationType { get; init; }

    // The annotation for Annotation, the ancestor for DescendantType.
    public string? AnnotationKey { get; init; }

    public required ulong Version { get; init; }

    public string? Author { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

// A template of a view without its content, for listings.
public record class ConfigurationTemplateSummary
{
    // Annotation type code (rst, sbt, …), as in ConfigurationTemplate.
    public required string AnnotationType { get; init; }

    public required ulong Version { get; init; }

    public string? Author { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
