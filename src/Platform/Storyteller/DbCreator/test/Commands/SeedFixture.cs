using System.IO.Abstractions.TestingHelpers;
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
// container factory that finds the layout fine unless told otherwise.
internal sealed class SeedFixture
{
    public static readonly string Tool = MockUnixSupport.Path(@"C:\tool");

    public SeedFixture()
    {
        var container = new Mock<Container>().Object;
        Containers.Setup(factory => factory.FindLayoutProblemsAsync()).ReturnsAsync(() => LayoutProblems.ToList());
        Containers.Setup(factory => factory.CreateContainerIfNotExistsAsync(It.IsAny<string>())).ReturnsAsync(container);
        Containers.Setup(factory => factory.EnsureOrganizationsContainerAsync()).ReturnsAsync(container);
    }

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
