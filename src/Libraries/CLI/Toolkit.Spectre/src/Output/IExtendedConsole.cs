using McMaster.Extensions.CommandLineUtils;
using Spectre.Console;

namespace _42.CLI.Toolkit.Output;

public interface IExtendedConsole : IRenderer, IPrompter, IProgressReporter
{
    /// <summary>Gets the McMaster console (for back-compat and host integration).</summary>
    IConsole Console { get; }

    /// <summary>Gets the Spectre.Console instance for native Spectre rendering.</summary>
    IAnsiConsole AnsiConsole { get; }
}
