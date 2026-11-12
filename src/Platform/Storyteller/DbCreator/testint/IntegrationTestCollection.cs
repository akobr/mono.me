namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<DbCreatorFixture>
{
    public const string Name = "DbCreator Integration";
}
