using Orleans.Runtime;

namespace _42.Crumble;

public class OrleansCrumbExecutionContext : ICrumbExecutionContext
{
    private readonly ObservableHashSet<string> _flags;
    private readonly ObservableDictionary<string, object> _properties;

    public OrleansCrumbExecutionContext()
    {
        _flags = new(StringComparer.Ordinal);
        _properties = new(StringComparer.Ordinal);
        InitializeRequestContextHandlers();
    }

    public OrleansCrumbExecutionContext(
        IEnumerable<string> flags,
        IEnumerable<KeyValuePair<string, object>> properties)
    {
        _flags = new(flags, StringComparer.Ordinal);
        _properties = new(properties, StringComparer.Ordinal);
        InitializeRequestContextHandlers();
    }

    public Guid Id { get; } = Guid.CreateVersion7();

    public string ContextKey { get; set; } = "default";

    public ISet<string> Flags => _flags;

    public IDictionary<string, object> Properties => _properties;

    public static OrleansCrumbExecutionContext FromRequestContext()
    {
        var interestingValues = RequestContext.Keys
            .Where(k => k.Length > 14
                        && k[13] is '.'
                        && k.StartsWith("#Crumble.", StringComparison.Ordinal))
            .GroupBy(k => k[9..13])
            .ToList();

        var flagKeys = (IEnumerable<string>)interestingValues.FirstOrDefault(g => g.Key == "Flag") ?? [];
        var propKeys = (IEnumerable<string>)interestingValues.FirstOrDefault(g => g.Key == "Prop") ?? [];

        return new OrleansCrumbExecutionContext(
            flagKeys.Select(k => k[14..]),
            propKeys.ToDictionary(k => k[14..], RequestContext.Get));
    }

    private void InitializeRequestContextHandlers()
    {
        _flags.ItemAdded += flag => { RequestContext.Set($"#Crumble.Flag.{flag}", true); };
        _flags.ItemRemoved += flag => { RequestContext.Remove($"#Crumble.Flag.{flag}"); };

        _properties.ItemAdded += (key, value) => { RequestContext.Set($"#Crumble.Prop.{key}", value); };
        _properties.ItemUpdated += (key, _, newValue) => { RequestContext.Set($"#Crumble.Prop.{key}", newValue); };
        _properties.ItemRemoved += (key, _) => { RequestContext.Remove($"#Crumble.Prop.{key}"); };
    }
}
