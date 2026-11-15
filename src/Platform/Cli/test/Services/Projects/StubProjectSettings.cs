using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Services.Projects;

namespace _42.Platform.Cli.UnitTests.Services.Projects;

// A provider of the project settings that gives one display and counts how often a command asked.
internal sealed class StubProjectSettings : IProjectSettingsProvider
{
    private readonly ProjectDisplay _display;

    public StubProjectSettings(ProjectDisplay display)
    {
        _display = display;
    }

    public int Calls { get; private set; }

    public Task<ProjectDisplay> GetDisplayAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(_display);
    }
}
