namespace _42.Platform.Storyteller.Accessing;

// Project settings that break the rules of ProjectSettingsRules (400 InvalidSettings): every problem, with its JSON path
// and rule, which the API returns as the Details of its answer.
public sealed class ProjectSettingsInvalidException : InvalidInputException
{
    public ProjectSettingsInvalidException(IReadOnlyList<ProjectSettingsProblem> problems)
        : base(MessageOf(problems), ErrorCodes.InvalidSettings)
    {
        Problems = problems;
    }

    public IReadOnlyList<ProjectSettingsProblem> Problems { get; }

    // The first problem, and how many there are.
    private static string MessageOf(IReadOnlyList<ProjectSettingsProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (problems.Count == 0)
        {
            return "The project settings are not valid.";
        }

        var first = $"{problems[0].Path}: {problems[0].Message}";
        return problems.Count == 1
            ? $"The project settings are not valid: {first}"
            : $"The project settings have {problems.Count} problems; the first is {first}";
    }
}
