using System;

namespace _42.Monorepo.Cli.Output
{
    public static class ProgressReporterExtensions
    {
        public static IProgress<double> StartProgress(
            this IProgressReporter reporter,
            Func<double, string> messageFactory)
        {
            return reporter
                .StartProgressBar(string.Empty)
                .AsProgress(messageFactory);
        }

        public static IProgress<T> StartProgress<T>(
            this IProgressReporter reporter,
            Func<T, string> messageFactory,
            Func<T, double?> percentageFactory)
        {
            return reporter
                .StartProgressBar(string.Empty)
                .AsProgress(messageFactory, percentageFactory);
        }
    }
}
