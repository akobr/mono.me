using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Services.Projects;

// The built-in names and dates, without a request: what a command shows when it is built without the provider (the
// container always gives it one).
public sealed class BuiltInProjectSettings : IProjectSettingsProvider
{
    public static BuiltInProjectSettings Instance { get; } = new();

    public Task<ProjectDisplay> GetDisplayAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ProjectDisplay.BuiltIn);
    }
}
