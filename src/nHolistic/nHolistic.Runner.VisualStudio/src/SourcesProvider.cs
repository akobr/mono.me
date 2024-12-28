namespace _42.tHolistic.Runner.VisualStudio;

public class SourcesProvider(IEnumerable<string>? sources) : ISourcesProvider
{
    private readonly IEnumerable<string> _sources = sources ?? [];

    public IEnumerable<string> GetSources()
    {
        return _sources;
    }
}
