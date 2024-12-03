using Newtonsoft.Json.Linq;

namespace _42.nHolistic;

public class TestCase
{
    public required string FullyQualifiedName { get; init; }

    public required string DisplayName { get; init; }

    public required string Source { get; init; }

    public JToken? Model { get; init; }

    public HashSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Dependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<TestCaseProperty> Traits { get; } = new();

    public List<TestCaseProperty> Properties { get; } = new();
}
