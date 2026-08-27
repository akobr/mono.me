namespace _42.Platform.Storyteller.Binding.Language;

public sealed class TextPart : BindingNode
{
    public TextPart(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
