namespace _42.Platform.Storyteller.Binding.Language;

public sealed class BindingSyntaxException : BindingException
{
    public BindingSyntaxException(string message, int position)
        : base($"{message} (at offset {position})")
    {
        Position = position;
    }

    public int Position { get; }
}
