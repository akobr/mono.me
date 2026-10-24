namespace _42.Platform.Storyteller.Mcp.Understanding;

// A merged document: the configuration of an annotation (AnnotationKey) or a template of the view (TypeCode). Values is
// the number of values of the explained document it supplies; it is left out where the source describes one value.
public sealed record ExplainedSource
{
    public required string Id { get; init; }

    public required Sdk.ConfigurationCompositionSourceKind Kind { get; init; }

    public string? AnnotationKey { get; init; }

    public string? TypeCode { get; init; }

    public long Version { get; init; }

    public Sdk.ConfigurationCompositionSourcePresence Presence { get; init; }

    public int? Values { get; init; }

    public static ExplainedSource From(Sdk.ConfigurationCompositionSource source, int? values = null)
    {
        var isTemplate = source.Kind == Sdk.ConfigurationCompositionSourceKind.Template;

        return new ExplainedSource
        {
            Id = source.Id,
            Kind = source.Kind,
            AnnotationKey = isTemplate ? null : source.AnnotationKey,
            TypeCode = isTemplate ? TypeCodeOf(source.Id) : null,
            Version = source.Version,
            Presence = source.Presence,
            Values = values,
        };
    }

    private static string? TypeCodeOf(string id)
    {
        var separator = id.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? null : id[(separator + 1)..];
    }
}
