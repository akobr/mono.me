using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetCatalogTests
{
    private static readonly string Tool = MockUnixSupport.Path(@"C:\tool");
    private static readonly string BuiltIn = Path.Combine(Tool, "data-sets");

    [Fact]
    public void GetSets_FindsTheBuiltInSetsNextToTheTool()
    {
        var fileSystem = new MockFileSystem();
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, "large"), ("set.json", "{}"));
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, "core"), ("set.json", """{ "Organization": "42dotnet" }"""));
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, ".draft"), ("set.json", "{}"));
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, "notes"), ("README.md", string.Empty));

        var catalog = new DataSetCatalog(fileSystem, Tool);
        var sets = catalog.GetSets();

        catalog.BuiltInFolder.ShouldBe(BuiltIn);
        sets.Select(set => (set.Name, set.Source)).ShouldBe([("core", DataSetSource.BuiltIn), ("large", DataSetSource.BuiltIn)]);
        sets[0].Folder.ShouldBe(Path.Combine(BuiltIn, "core"));
        sets[0].Manifest.Organization.ShouldBe("42dotnet");
    }

    [Fact]
    public void GetSets_HasNoBuiltInSetsWithoutTheirFolder()
    {
        new DataSetCatalog(new MockFileSystem(), Tool).GetSets().ShouldBeEmpty();
    }

    [Fact]
    public void GetSets_AddsAFolderWithSetJsonAsOneSetAndOtherwiseEachOfItsSets()
    {
        var fileSystem = new MockFileSystem();
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, "core"), ("set.json", "{}"));
        SetFiles.Add(fileSystem, SetFiles.Folder("playground"), ("set.json", """{ "Name": "playground" }"""), ("sub/set.json", """{ "Name": "nested" }"""));
        SetFiles.Add(fileSystem, SetFiles.Folder("many", "b-set"), ("set.json", "{}"));
        SetFiles.Add(fileSystem, SetFiles.Folder("many", "a-set"), ("set.json", "{}"));
        SetFiles.Add(fileSystem, SetFiles.Folder("many", "docs"), ("README.md", string.Empty));

        var sets = new DataSetCatalog(fileSystem, Tool).GetSets([SetFiles.Folder("playground"), SetFiles.Folder("many")]);

        sets.Select(set => (set.Name, set.Source)).ShouldBe(
        [
            ("core", DataSetSource.BuiltIn),
            ("playground", DataSetSource.Path),
            ("a-set", DataSetSource.Path),
            ("b-set", DataSetSource.Path),
        ]);
        sets[2].Folder.ShouldBe(SetFiles.Folder("many", "a-set"));
    }

    [Fact]
    public void GetSets_ResolvesARelativeFolderFromTheCurrentOne()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), SetFiles.Root);
        SetFiles.Add(fileSystem, SetFiles.Folder("relative"), ("set.json", "{}"));

        new DataSetCatalog(fileSystem, Tool).GetSets(["relative"]).ShouldHaveSingleItem().Folder.ShouldBe(SetFiles.Folder("relative"));
    }

    [Fact]
    public void GetSets_RefusesAFolderThatIsMissingOrHoldsNoSet()
    {
        var fileSystem = new MockFileSystem();
        SetFiles.Add(fileSystem, SetFiles.Folder("empty"), ("README.md", string.Empty), ("docs/notes.md", string.Empty));
        var catalog = new DataSetCatalog(fileSystem, Tool);

        Should.Throw<DataSetException>(() => catalog.GetSets([SetFiles.Folder("missing")])).Message.ShouldContain("does not exist");
        Should.Throw<DataSetException>(() => catalog.GetSets([SetFiles.Folder("empty")])).Message.ShouldContain("holds no set");
    }

    [Fact]
    public void GetSets_RefusesANameFoundTwice()
    {
        var fileSystem = new MockFileSystem();
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, "core"), ("set.json", "{}"));
        SetFiles.Add(fileSystem, SetFiles.Folder("my-core"), ("set.json", """{ "Name": "core" }"""));

        var exception = Should.Throw<DataSetException>(() => new DataSetCatalog(fileSystem, Tool).GetSets([SetFiles.Folder("my-core")]));

        exception.Message.ShouldContain("'core' is found twice");
        exception.Message.ShouldContain(SetFiles.Folder("my-core"));
    }

    [Fact]
    public void GetSets_RefusesAnInvalidManifest()
    {
        var fileSystem = new MockFileSystem();
        SetFiles.Add(fileSystem, Path.Combine(BuiltIn, "core"), ("set.json", """{ "Unknown": 1 }"""));

        Should.Throw<DataSetException>(() => new DataSetCatalog(fileSystem, Tool).GetSets()).Message.ShouldContain("Unknown");
    }

    [Fact]
    public void Resolve_SeedsTheDependenciesFirstAndEachSetOnce()
    {
        var sets = Sets(("a", ["b", "c"]), ("b", ["c"]), ("c", []), ("d", ["c"]), ("e", []));

        Names(DataSetCatalog.Resolve(sets, ["a", "d"])).ShouldBe(["c", "b", "a", "d"]);
        Names(DataSetCatalog.Resolve(sets, ["e", "c", "a", "a"])).ShouldBe(["e", "c", "b", "a"]);
        Names(DataSetCatalog.Resolve(sets, [" d "])).ShouldBe(["c", "d"]);
    }

    [Fact]
    public void Resolve_RefusesACycle()
    {
        var sets = Sets(("a", ["b"]), ("b", ["c"]), ("c", ["a"]), ("self", ["self"]), ("top", ["a"]));

        Should.Throw<DataSetException>(() => DataSetCatalog.Resolve(sets, ["top"])).Message.ShouldContain("a cycle: a -> b -> c -> a");
        Should.Throw<DataSetException>(() => DataSetCatalog.Resolve(sets, ["self"])).Message.ShouldContain("a cycle: self -> self");
    }

    [Fact]
    public void Resolve_RefusesANameOrADependencyNotInTheCatalog()
    {
        var sets = Sets(("showcase", ["core"]), ("large", []));

        Should.Throw<DataSetException>(() => DataSetCatalog.Resolve(sets, ["missing"]))
            .Message.ShouldBe("The set 'missing' is not in the catalog (it has large, showcase).");
        Should.Throw<DataSetException>(() => DataSetCatalog.Resolve(sets, ["showcase"]))
            .Message.ShouldBe("The set 'showcase' depends on 'core', which is not in the catalog (it has large, showcase).");
        Should.Throw<DataSetException>(() => DataSetCatalog.Resolve([], ["core"]))
            .Message.ShouldBe("The set 'core' is not in the catalog (it has no sets).");
    }

    private static List<DataSet> Sets(params (string Name, string[] DependsOn)[] sets)
    {
        return sets
            .Select(set => new DataSet(new DataSetManifest { Name = set.Name, DependsOn = set.DependsOn }, SetFiles.Folder(set.Name), DataSetSource.Path))
            .ToList();
    }

    private static IEnumerable<string> Names(IEnumerable<DataSet> sets)
    {
        return sets.Select(set => set.Name);
    }
}
