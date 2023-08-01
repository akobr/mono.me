namespace _42.CLI.Toolkit.Output
{
    public static class TreeOutputBuilderExtensions
    {
        public static string BuildTree(this ITreeOutputBuilder builder, IComposition<string> root, int leftIndentation = 0)
        {
            return builder.BuildTree(root, t => t, leftIndentation);
        }

        public static string BuildTree(this ITreeOutputBuilder builder, IComposition<object> root, int leftIndentation = 0)
        {
            return builder.BuildTree(root, o => o.ToString() ?? string.Empty, leftIndentation);
        }
    }
}
