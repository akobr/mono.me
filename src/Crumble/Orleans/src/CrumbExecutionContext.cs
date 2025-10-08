namespace _42.Crumble;

public class CrumbExecutionContext : ICrumbExecutionContext
{
    public string ContextKey { get; set; } = "default";

    public ISet<string> Flags { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>(StringComparer.Ordinal);
}
