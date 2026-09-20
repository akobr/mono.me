namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<CosmosFixture>
{
    public const string Name = "CosmosDb Integration";
}
