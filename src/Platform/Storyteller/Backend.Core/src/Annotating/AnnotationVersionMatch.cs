namespace _42.Platform.Storyteller.Annotating;

// The versions an annotation write is conditioned on (If-Match, If-None-Match; RFC 9110 §13.1): Any (*) or a list of
// storage markers. A tag that names no marker the service knows is left out, so a list can be empty and then matches
// nothing.
public sealed record AnnotationVersionMatch(bool Any, IReadOnlyList<string> Markers)
{
    public static AnnotationVersionMatch Anything { get; } = new(true, []);

    public static AnnotationVersionMatch Of(params string[] markers) => new(false, markers);

    // Whether an existing annotation of this marker is matched.
    public bool Matches(string? marker)
    {
        return Any || (marker is not null && Markers.Contains(marker, StringComparer.Ordinal));
    }
}
