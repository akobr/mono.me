using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Services.Projects;

// The project settings as the console output shows them (spec section 7.3). A command that shows a type or a date to a
// person asks once, after its own call of the API, and only for its human output, never for JSON, files or programs.
public interface IProjectSettingsProvider
{
    // The display of the context project: ProjectDisplay.BuiltIn without a console, when switched off, and on any
    // failure of the read; never throws.
    Task<ProjectDisplay> GetDisplayAsync(CancellationToken cancellationToken = default);
}
