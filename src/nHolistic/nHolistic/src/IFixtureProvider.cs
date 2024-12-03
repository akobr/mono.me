namespace _42.nHolistic;

public interface IFixtureProvider
{
    bool TryGetFixture<TFixture>(string key, out TFixture fixture);

    bool TryGetFixture(string key, out object fixture);

    IEnumerable<object> GetFixtures(string testCaseKey);
}
