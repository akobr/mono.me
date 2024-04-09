namespace _42.Platform.Storyteller;

public interface IUnit : IAnnotation
{
    string ResponsibilityKey { get; }

    string ResponsibilityName { get; }
}
