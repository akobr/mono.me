using System;

namespace _42.CLI.Toolkit.Output;

public static class ProgressReporterExtensions
{
    public static IProgress<double> AsProgress(this IProgressBar bar, Func<double, string> messageFactory)
    {
        return new Progress<double>(value =>
        {
            var percentage = (int)(value * 100);
            bar.Tick(percentage, messageFactory(value));
        });
    }

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
        var bar = reporter.StartProgressBar(string.Empty);
        return new Progress<T>(value =>
        {
            var percentage = (int)((percentageFactory(value) ?? 0) * 100);
            bar.Tick(percentage, messageFactory(value));
        });
    }
}
