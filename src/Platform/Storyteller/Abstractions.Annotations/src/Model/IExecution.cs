namespace _42.Platform.Storyteller;

public interface IExecution : IAnnotation
{
    string ResponsibilityKey { get; }

    string SubjectKey { get; }

    string ContextKey { get; }

    string SubjectName { get; }

    string ResponsibilityName { get; }

    string ContextName { get; }
}
