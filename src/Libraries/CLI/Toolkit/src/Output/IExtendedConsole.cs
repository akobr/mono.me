using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit.Output;

public interface IExtendedConsole : IRenderer, IPrompter, IProgressReporter
{
    public IConsole Console { get; }
}
