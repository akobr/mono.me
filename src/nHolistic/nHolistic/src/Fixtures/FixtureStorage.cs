using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace _42.nHolistic;

public class FixtureStorage(
    ITestCasesMapper mapper,
    ITypeActivator activator) : IFixtureStorage
{
    private readonly ConcurrentDictionary<string, (Type Type, object? Instance)> _fixtures = new(StringComparer.Ordinal);

    public bool TryGetFixture<TFixture>(string label, [MaybeNullWhen(false)]out TFixture fixture)
    {
        if (!_fixtures.TryGetValue(label, out var fixturePair))
        {
            fixture = default;
            return false;
        }

        var type = typeof(TFixture);

        if (!type.IsAssignableFrom(fixturePair.Type))
        {
            fixture = default;
            return false;
        }

        fixturePair.Instance ??= activator.Activate(fixturePair.Type, null);
        fixture = (TFixture)fixturePair.Instance;
        return true;
    }

    public bool TryGetFixture(string label, [MaybeNullWhen(false)]out object fixture)
    {
        if (!_fixtures.TryGetValue(label, out var fixturePair))
        {
            fixture = null;
            return false;
        }

        fixturePair.Instance ??= activator.Activate(fixturePair.Type, null);
        fixture = fixturePair.Instance;
        return true;
    }

    public IEnumerable<object> GetFixtures(string testCaseKey)
    {
        var testCase = mapper.GetTestCases(testCaseKey).First();

        if (TryGetFixtureForTestCase(testCase.Case, out var specificFixture))
        {
            yield return specificFixture;
        }

        foreach (var label in testCase.Case.Labels)
        {
            if (TryGetFixture(label, out var fixture))
            {
                yield return fixture;
            }
        }
    }

    public void RegisterFixture(string label, Type fixtureType)
    {
        // TODO: [P2] add log if the label is already registered
        _fixtures.TryAdd(label, (fixtureType, null));
    }

    // TODO: [P1] this needs to die, there is never a fixture in context of specific test (method)
    private bool TryGetFixtureForTestCase(TestCase testCase, [MaybeNullWhen(false)] out object fixture)
    {
        if (!_fixtures.TryGetValue(testCase.FullyQualifiedName, out var fixturePair))
        {
            fixture = null;
            return false;
        }

        fixturePair.Instance ??= activator.Activate(fixturePair.Type, testCase);
        fixture = fixturePair.Instance;
        return true;
    }
}
