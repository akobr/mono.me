using System.Diagnostics.CodeAnalysis;

namespace _42.nHolistic;

public interface IFixtureProvider
{
    bool TryGetFixture<TFixture>(string label, [MaybeNullWhen(false)]out TFixture fixture);

    bool TryGetFixture(string label, [MaybeNullWhen(false)]out object fixture);

    IEnumerable<object> GetFixtures(string testCaseKey);
}
