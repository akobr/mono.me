namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// One external input of a resolve: what was read (<see cref="Kind"/>, <see cref="Source"/>, <see cref="Key"/>) and an
/// opaque version of what the read saw (<see cref="Marker"/>). A host revalidates the markers to tell whether a resolved
/// document is still current without evaluating it again (see <see cref="BindingDependencies"/>).
/// </summary>
/// <param name="Kind">The kind of input, e.g. <c>kv</c> for a key/value pair.</param>
/// <param name="Source">Where the input lives, e.g. the view of a key/value pair.</param>
/// <param name="Key">The input within its source, e.g. the normalized key.</param>
/// <param name="Marker">The storage version of the input when it was read, or <see cref="Absent"/> when nothing was stored.</param>
public sealed record BindingDependency(string Kind, string Source, string Key, string Marker)
{
    /// <summary>
    /// The marker of an input that was not stored. A Cosmos <c>_etag</c> is a quoted string, so it never equals this.
    /// </summary>
    public const string Absent = "absent";
}
