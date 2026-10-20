using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

// How the effective configuration of an annotation is composed: every document that is merged, the calculations that
// group the merges, the merges in the order of application, and the step that set each value of the result.
public record class ConfigurationComposition
{
    public required string AnnotationKey { get; init; }

    // The effective document, composed from the sources below.
    public required JObject Content { get; init; }

    // Hash of Content, calculated as the Effective read calculates it; null for an empty document.
    public string? Hash { get; init; }

    // Every document that takes part, once, even when it is applied more than once.
    public required IReadOnlyList<ConfigurationCompositionSource> Sources { get; init; }

    // The calculations, in the order they finish; the target is the last one.
    public required IReadOnlyList<ConfigurationCompositionNode> Nodes { get; init; }

    // The merges of source documents, in the order of application.
    public required IReadOnlyList<ConfigurationCompositionStep> Steps { get; init; }

    // The step that set each value of Content, in document order.
    public required IReadOnlyList<ConfigurationCompositionOrigin> Origins { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationCompositionSourceKind
{
    Configuration = 0,
    Template,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationCompositionSourcePresence
{
    // No stored configuration (no item, or an item that holds only the cache); it is applied as nothing.
    Missing = 0,

    // A stored document without properties.
    Empty,

    Present,
}

public record class ConfigurationCompositionSource
{
    // "configuration:{annotationKey}" or "template:{typeCode}".
    public required string Id { get; init; }

    public required ConfigurationCompositionSourceKind Kind { get; init; }

    // The type of the annotation, or the type the template is for.
    public required AnnotationType AnnotationType { get; init; }

    // The annotation of a configuration; null for a template.
    public string? AnnotationKey { get; init; }

    public required ConfigurationCompositionSourcePresence Presence { get; init; }

    // 0 when Missing.
    public required ulong Version { get; init; }

    // Null when Missing.
    public AuditStamp? Modified { get; init; }

    // The stored document; null when Missing.
    public JObject? Content { get; init; }
}

// One calculation of the inheritance graph. A shared ancestor is calculated once per branch, so it has a node per branch.
public record class ConfigurationCompositionNode
{
    // The position in Nodes.
    public required int Index { get; init; }

    public required string AnnotationKey { get; init; }

    // The node whose calculation merges the result of this one; null for the target.
    public int? MergedInto { get; init; }

    // 0 for the target, 1 for its direct ancestors, and so on.
    public required int Depth { get; init; }
}

public record class ConfigurationCompositionStep
{
    // The order of application, from 1.
    public required int Order { get; init; }

    public required string SourceId { get; init; }

    // The node whose calculation applies the source.
    public required int Node { get; init; }
}

public record class ConfigurationCompositionOrigin
{
    // RFC 6901 JSON Pointer into Content, for example "/database/hosts/0".
    public required string Path { get; init; }

    // The order of the step that set the value.
    public required int Step { get; init; }
}
