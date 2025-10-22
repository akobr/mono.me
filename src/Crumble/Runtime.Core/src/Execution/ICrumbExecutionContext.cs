namespace _42.Crumble;

public interface ICrumbExecutionContext
{
    Guid Id { get; }

    public string ContextKey { get; }

    public ISet<string> Flags { get; }

    public IDictionary<string, object> Properties { get; }
}
