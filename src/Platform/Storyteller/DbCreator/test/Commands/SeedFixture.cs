using System.IO.Abstractions.TestingHelpers;
using System.Net;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Protection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// The services of the commands over fakes: built-in sets in a MockFileSystem, the access model and the views of Moq
// (nothing exists, every write succeeds), the project settings, importer, bulk writer, key/value service and record
// store of the seeder tests (no key-encryption provider), and a container factory that finds the layout fine unless
// told otherwise, with a container where every item is missing (the access seed of the root reads and creates; each
// access point with its access audit record in a transactional batch, which succeeds).
internal sealed class SeedFixture
{
    public static readonly string Tool = MockUnixSupport.Path(@"C:\tool");

    public SeedFixture()
    {
        Container
            .Setup(container => container.ReadItemAsync<It.IsAnyType>(It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Throws(new CosmosException("The item does not exist.", HttpStatusCode.NotFound, 0, string.Empty, 0));
        Container.Setup(container => container.CreateTransactionalBatch(It.IsAny<PartitionKey>())).Returns(Batch.Object);
        Batch
            .Setup(batch => batch.CreateItem(It.IsAny<It.IsAnyType>(), It.IsAny<TransactionalBatchItemRequestOptions>()))
            .Returns(Batch.Object);
        var response = new Mock<TransactionalBatchResponse>();
        response.Setup(result => result.IsSuccessStatusCode).Returns(true);
        response.Setup(result => result.StatusCode).Returns(HttpStatusCode.OK);
        Batch.Setup(batch => batch.ExecuteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response.Object);
        Containers.Setup(factory => factory.FindLayoutProblemsAsync()).ReturnsAsync(() => LayoutProblems.ToList());
        Containers.Setup(factory => factory.CreateContainerIfNotExistsAsync(It.IsAny<string>())).ReturnsAsync(Container.Object);
        Containers.Setup(factory => factory.EnsureOrganizationsContainerAsync()).ReturnsAsync(Container.Object);
    }

    public Mock<Container> Container { get; } = new();

    // Every batch of the container: the items of all of them accumulate in its invocations.
    public Mock<TransactionalBatch> Batch { get; } = new();

    public MockFileSystem FileSystem { get; } = new();

    public Mock<IAccessService> Access { get; } = new();

    public Mock<IViewService> Views { get; } = new();

    public FakeProjectSettingsService ProjectSettings { get; } = new();

    public Mock<IContainerFactory> Containers { get; } = new();

    public FakeViewImporter Importer { get; } = new();

    public FakeBulkWriter BulkWriter { get; } = new();

    public FakeRecordStore Records { get; } = new();

    public FakeKeyValueService KeyValues { get; } = new();

    // the key-encryption providers of the tool: none, so Standard pairs are refused
    public List<IKeyEncryptionProvider> Providers { get; } = [];

    public List<string> LayoutProblems { get; } = [];

    public BaseAccountOptions BaseAccount { get; } = new();

    public DataSetCatalog Catalog => new(FileSystem, Tool);

    public DataSetSeeder Seeder()
    {
        return new DataSetSeeder(
            FileSystem,
            Catalog,
            new DataSetProvisioner(Access.Object, Views.Object, NullLogger<DataSetProvisioner>.Instance),
            ProjectSettings,
            Importer,
            BulkWriter,
            new DataSetKeyValueSeeder(KeyValues, Providers, Options.Create(new KeyValueOptions()), NullLogger<DataSetKeyValueSeeder>.Instance),
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
