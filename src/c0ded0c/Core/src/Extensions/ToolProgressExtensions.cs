using System;

namespace c0ded0c.Core
{
    public static class ToolProgressExtensions
    {
        public static void Report(this IProgress<IToolProgress>? progress, string message)
        {
            progress?.Report(new ToolProgress(message));
        }

        public static void Report(this IProgress<IToolProgress>? progress, string message, double percentages)
        {
            progress?.Report(new ToolProgress(message, percentages));
        }

        public static void Report(this IProgress<IToolProgress>? progress, string message, double percentages, string category)
        {
            progress?.Report(new ToolProgress(message, percentages, category));
        }
    }
}
