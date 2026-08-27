namespace _42.Platform.Storyteller.Binding;

public sealed class BindingRequest
{
    public required IReadOnlyList<string> Path { get; init; }

    public required bool IncludeSecrets { get; init; }

    public string PathString => string.Join('.', Path);
}
