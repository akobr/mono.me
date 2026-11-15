namespace _42.Platform.Storyteller;

// Why a settings document is refused: the JSON path of the value ($.Types.sbt.Color, in the canonical spelling of a known
// member or type code), the rule (one of the rule ids of ProjectSettingsRules, as the conformance vectors spell them) and
// a sentence for people.
public sealed record class ProjectSettingsProblem(string Path, string Rule, string Message);
