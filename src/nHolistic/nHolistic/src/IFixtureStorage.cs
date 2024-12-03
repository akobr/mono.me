namespace _42.nHolistic;

public interface IFixtureStorage : IFixtureProvider
{
    void RegisterFixture<TFixture>(string key, TFixture fixture);
}
