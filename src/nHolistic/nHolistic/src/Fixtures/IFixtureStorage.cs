namespace _42.tHolistic;

public interface IFixtureStorage : IFixtureProvider
{
    void RegisterFixture(string label, Type fixtureType);
}
