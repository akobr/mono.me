using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public abstract class BaseTestsClass(Startup startup)
    : IClassFixture<Startup>
{
    protected ITestContext Context => startup;
}
