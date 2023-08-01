using Alba.CsConsoleFormat;

namespace _42.CLI.Toolkit.Output
{
    public interface IHeaderColumn
    {
        object[] Content { get; }

        GridLength Size { get; }
    }

    public class HeaderColumn : IHeaderColumn
    {
        public HeaderColumn(string text)
        {
            Content = new object[] { new Span(text) };
            Size = GridLength.Auto;
        }

        public HeaderColumn(string text, GridLength size)
        {
            Content = new object[] { new Span(text) };
            Size = size;
        }

        public HeaderColumn(object[] content)
        {
            Content = content;
            Size = GridLength.Auto;
        }

        public HeaderColumn(object[] content, GridLength size)
        {
            Content = content;
            Size = size;
        }

        public object[] Content { get; }

        public GridLength Size { get; }

        public static implicit operator HeaderColumn(string text)
        {
            return new HeaderColumn(text);
        }

        public static implicit operator HeaderColumn(object[] content)
        {
            return new HeaderColumn(content);
        }
    }
}
