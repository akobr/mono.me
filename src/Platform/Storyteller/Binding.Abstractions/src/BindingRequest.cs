namespace _42.Platform.Storyteller.Binding;

public sealed class BindingRequest
{
    public required IReadOnlyList<string> Path { get; init; }

    public required bool IncludeSecrets { get; init; }

    /// <summary>
    /// The <see cref="BindingScope.Context"/> of this resolution pass (e.g. the configuration being resolved),
    /// or <c>null</c> when none was supplied.
    /// </summary>
    public object? Context { get; init; }

    /// <summary>
    /// The <see cref="BindingScope.Dependencies"/> of this resolution pass: an <see cref="IRecordingBinding"/> source
    /// records what it reads into it. <c>null</c> when the pass does not record.
    /// </summary>
    public BindingDependencies? Dependencies { get; init; }

    public string PathString => string.Join('.', Path);
}
