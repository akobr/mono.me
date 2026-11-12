using System.IO.Abstractions.TestingHelpers;
using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// The services of the commands over fakes: built-in sets in a MockFileSystem, the access model and the views of Moq
// (nothing exists, every write succeeds), the importer, bulk writer and record store of the seeder tests, and a
// container factory that finds the layout fine unless told otherwise, with a container where every item is missing
// (the access seed of the root reads and creates).
internal sealed class SeedFixture
{
    public static readonly string Tool = MockUnixSupport.Path(@"C:\tool");

    public SeedFixture()
    {
        Container
            .Setup(container => container.ReadItemAsync<It.IsAnyType>(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Throws(new CosmosException("The item does not exist.", HttpStatusCode.NotFound, 0, string.Empty, 0));
        Containers.Setup(factory => factory.FindLayoutProblemsAsync()).ReturnsAsync(() => LayoutProblems.ToList());
        Containers.Setup(factory => factory.CreateContainerIfNotExistsAsync(It.IsAny<string>())).ReturnsAsync(Container.Object);
        Containers.Setup(factory => factory.EnsureOrganizationsContainerAsync()).ReturnsAsync(Container.Object);
    }

    public Mock<Container> Container { get; } = new();

    public MockFileSystem FileSystem { get; } = new();

    public Mock<IAccessService> Access { get; } = new();

    public Mock<IViewService> Views { get; } = new();

    public Mock<IContainerFactory> Containers { get; } = new();

    public FakeViewImporter Importer { get; } = new();

    public FakeBulkWriter BulkWriter { get; } = new();

    public FakeRecordStore Records { get; } = new();

    public List<string> LayoutProblems { get; } = [];

    public BaseAccountOptions BaseAccount { get; } = new();

    public DataSetCatalog Catalog => new(FileSystem, Tool);

    public DataSetSeeder Seeder()
    {
        return new DataSetSeeder(
            FileSystem,
            Catalog,
            new DataSetProvisioner(Access.Object, Views.Object, NullLogger<DataSetProvisioner>.Instance),
            Importer,
            BulkWriter,
            Records,
            Options.Create(BaseAccount),
            TimeProvider.System,
            NullLogger<DataSetSeeder>.Instance);
    }

    public CoreDbStructureBuilder Structure()
    {
        return new CoreDbStructureBuilder(Containers.Object, Seeder(), Options.Create(BaseAccount), Options.Create(new StagingSeedOptions()));
    }

    public string AddSet(string name, string manifest, params (string Path, string Content)[] files)
    {
        var folder = Path.Combine(Tool, DataSetCatalog.BuiltInFolderName, name);
        SetFiles.Add(FileSystem, folder, [("set.json", manifest), .. files]);
        return folder;
    }
}
