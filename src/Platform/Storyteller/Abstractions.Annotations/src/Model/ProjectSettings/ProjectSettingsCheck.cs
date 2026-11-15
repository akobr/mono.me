using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller;

// The result of validating a settings document: the normalized settings, or every problem and no settings. Nothing is
// dropped silently; a document with a problem has no settings.
public sealed record class ProjectSettingsCheck(ProjectSettings? Settings, IReadOnlyList<ProjectSettingsProblem> Problems)
{
    [MemberNotNullWhen(true, nameof(Settings))]
    public bool IsValid => Settings is not null;
}
