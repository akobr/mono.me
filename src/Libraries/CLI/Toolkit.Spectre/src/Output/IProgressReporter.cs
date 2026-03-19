using System;

namespace _42.CLI.Toolkit.Output;

public interface IProgressBar : IDisposable
{
    void Tick(int value, string? message = null);

    IProgressBar Spawn(string message, int maxTicks = 100);
}

public interface IProgressReporter
{
    bool HasMainProgressBar { get; }

    IProgressBar? GetMainProgressBar();

    void EndMainProgressBar();

    IProgressBar StartProgressBar(string message, int maxTicks = 100);
}
