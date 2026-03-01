namespace _42.Platform.Storyteller;

public interface IUnitOfExecution : IAnnotation
{
    string ResponsibilityKey { get; }

    string UnitKey { get; }

    string SubjectKey { get; }

    string ContextKey { get; }

    string ResponsibilityName { get; }

    string UnitName { get; }

    string SubjectName { get; }

    string ContextName { get; }
}
