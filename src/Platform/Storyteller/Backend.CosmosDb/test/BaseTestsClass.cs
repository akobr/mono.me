using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

[Collection("CosmosDb")]
public abstract class BaseTestsClass(Startup startup)
{
    protected ITestContext Context => startup;
}
