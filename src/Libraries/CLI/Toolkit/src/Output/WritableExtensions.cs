namespace _42.CLI.Toolkit.Output;

public static class WritableExtensions
{
    public static ConsoleOutput Highlight(this string @this)
    {
        return new ConsoleOutput(new ConsoleOutputThemedText(@this, t => t.HighlightColor));
    }

    public static ConsoleOutput Lowlight(this string @this)
    {
        return new ConsoleOutput(new ConsoleOutputThemedText(@this, t => t.LowlightColor));
    }
}
