namespace _42.Crumble;

public class CrumbExecutionContext : ICrumbExecutionContext
{
    public string ContextKey { get; set; }

    public ISet<string> Flags { get; set; }
    
    public IDictionary<string, object> Properties { get; set; }
}
