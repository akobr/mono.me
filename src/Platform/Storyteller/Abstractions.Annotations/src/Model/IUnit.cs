namespace _42.Platform.Storyteller;

public interface IUnit : IAnnotation
{
    string ResponsibilityKey { get; }

    string ResponsibilityName { get; }

    /// <summary>
    /// What kind of trigger the unit is. Any string is stored. Common values are <c>time</c>, <c>message</c>, and <c>volume</c>.
    /// </summary>
    string UnitType { get; }

    /// <summary>
    /// The concrete trigger. Any string is stored: a Cron expression, a message name rule, template, or source name, or a volume source or path filter.
    /// </summary>
    string UnitDefinition { get; }
}
