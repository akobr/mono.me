using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public sealed class BindingFunctionRequest
{
    public required string Name { get; init; }

    public required IReadOnlyList<BindingValue> Arguments { get; init; }

    public required bool IncludeSecrets { get; init; }

    /// <summary>
    /// The <see cref="BindingScope.Document"/> supplied by the caller of <see cref="IBindingExecutor"/> for this
    /// resolution pass, or <c>null</c> when none was supplied.
    /// </summary>
    public JToken? Document { get; init; }

    /// <summary>
    /// The <see cref="BindingScope.Context"/> supplied by the caller of <see cref="IBindingExecutor"/> for this
    /// resolution pass, or <c>null</c> when none was supplied.
    /// </summary>
    public object? Context { get; init; }
}
