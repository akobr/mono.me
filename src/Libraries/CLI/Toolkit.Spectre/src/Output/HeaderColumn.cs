namespace _42.CLI.Toolkit.Output;

public interface IHeaderColumn
{
    string Content { get; }

    int? Width { get; }
}

public class HeaderColumn : IHeaderColumn
{
    public HeaderColumn(string content)
    {
        Content = content;
        Width = null;
    }

    public HeaderColumn(string content, int width)
    {
        Content = content;
        Width = width;
    }

    public string Content { get; }

    public int? Width { get; }

    public static implicit operator HeaderColumn(string text)
    {
        return new HeaderColumn(text);
    }
}
