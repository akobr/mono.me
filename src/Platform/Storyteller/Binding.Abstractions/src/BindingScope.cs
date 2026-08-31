using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// Ambient data supplied by the caller of <see cref="IBindingExecutor"/> that a <see cref="IBindingFunction"/> may
/// use in addition to its own arguments. Unlike a source-backed statement's resolution, this data is not looked up
/// through the binding language itself; it is provided directly by the host (e.g. the configuration service) once
/// per resolution pass.
/// </summary>
public sealed class BindingScope
{
    /// <summary>
    /// The document a function may query in its entirety, e.g. a snapshot of the configuration currently being
    /// resolved. <c>null</c> when the caller has no such document to offer.
    /// </summary>
    public JToken? Document { get; init; }

    /// <summary>
    /// Opaque, consumer-defined context. A specific <see cref="IBindingFunction"/> implementation may cast this to
    /// whatever type its host supplies (e.g. the key identifying the configuration being resolved). <c>null</c>
    /// when the caller has no such context to offer.
    /// </summary>
    public object? Context { get; init; }
}
