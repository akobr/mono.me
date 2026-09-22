using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

[CollectionDefinition("CosmosDb")]
public class CosmosDbCollection : ICollectionFixture<Startup>
{
}
