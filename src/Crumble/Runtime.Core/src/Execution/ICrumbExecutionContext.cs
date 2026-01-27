namespace _42.Crumble;

public interface ICrumbExecutionContext
{
    Guid Id { get; }

    string ContextKey { get; }

    ISet<string> Flags { get; }

    IDictionary<string, object> Properties { get; }
}
