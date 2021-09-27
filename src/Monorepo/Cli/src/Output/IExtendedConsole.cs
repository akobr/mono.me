using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Output
{
    public interface IExtendedConsole : IRenderer, IPrompter, IProgressReporter
    {
        public IConsole Console { get; }
    }
}
