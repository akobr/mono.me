using System;

namespace _42.Platform.Storyteller.Entities.Access;

// How a project shows one annotation type, as ProjectSettingsEntity.Types stores it; a missing value is the built-in one.
public record class ProjectTypeAppearanceEntity
{
    public string? Label { get; init; }

    public string? Plural { get; init; }

    public string? Color { get; init; }

    public string? Icon { get; init; }

    public static ProjectTypeAppearanceEntity From(ProjectTypeAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        return new ProjectTypeAppearanceEntity
        {
            Label = appearance.Label,
            Plural = appearance.Plural,
            Color = appearance.Color,
            Icon = appearance.Icon,
        };
    }

    public ProjectTypeAppearance ToModel() => new()
    {
        Label = Label,
        Plural = Plural,
        Color = Color,
        Icon = Icon,
    };
}
