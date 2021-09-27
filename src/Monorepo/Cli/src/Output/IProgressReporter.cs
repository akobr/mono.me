using ShellProgressBar;

namespace _42.Monorepo.Cli.Output
{
    public interface IProgressReporter
    {
        bool HasMainProgressBar { get; }

        IProgressBar? GetMainProgressBar();

        void EndMainProgressBar();

        public IProgressBar StartProgressBar(string message, ProgressBarOptions? options = null);
    }
}
