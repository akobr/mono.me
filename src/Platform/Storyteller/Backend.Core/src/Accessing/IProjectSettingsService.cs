using System.Text.Json.Nodes;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.Accessing;

// The settings of a project (ProjectSettings), stored on its access point next to its members and policies. Every write
// is validated by ProjectSettingsRules, raises Version, stamps Modified and appends a ProjectSettingsChange event to the
// project's audit trail; a write whose result equals the stored settings writes nothing and keeps the version.
public interface IProjectSettingsService
{
    // The stored settings, or the defaults (Version 0) when nothing was stored. NotFoundException for an unknown project.
    Task<ProjectSettings> GetAsync(string organization, string project, CancellationToken cancellationToken = default);

    // Replaces Types, DefaultTimeZone and DateFormat (Version and Modified of the input are ignored): a type left out goes
    // back to its built-in appearance, a missing time zone or date format to its default. ProjectSettingsInvalidException
    // with every problem, ProjectSettingsPreconditionFailedException when the condition does not hold, NotFoundException
    // for an unknown project, ConflictException when the project changed concurrently twice in a row.
    Task<ProjectSettingsWrite> SetAsync(
        string organization,
        string project,
        ProjectSettings settings,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        CancellationToken cancellationToken = default);

    // Applies an RFC 7396 merge patch to the stored settings (in their Api JSON shape, ProjectSettingsJson.ReadMergePatch)
    // and validates the result as SetAsync does, with the same exceptions; a name of the patch that is no member or no
    // type code, or that differs from another one only in case, is a problem too.
    Task<ProjectSettingsWrite> PatchAsync(
        string organization,
        string project,
        JsonObject patch,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        CancellationToken cancellationToken = default);
}
