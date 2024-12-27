namespace _42.nHolistic;

public interface IFixtureStorage : IFixtureProvider
{
    void RegisterFixture(string label, Type fixtureType);
}
