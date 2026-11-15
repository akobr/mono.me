using System.IO.Abstractions;
using System.Text.Json;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The sets of the repository, as copied next to the tool (and to this project's output).
public class BuiltInSetsTests
{
    private static readonly IFileSystem FileSystem = new FileSystem();

    [Fact]
    public void Core_IsTheAnnotationsAndTheConfigurationOfThePlatformItself()
    {
        var core = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == CoreDbStructureBuilder.CORE_SET);

        core.Source.ShouldBe(DataSetSource.BuiltIn);
        core.Manifest.DependsOn.ShouldBeEmpty();
        DataSetTarget.Resolve([core]).ShouldBe(new DataSetTarget("42dotnet", "main", "default"));

        var content = DataSetContent.Read(FileSystem, core.Folder, DataSetTarget.Resolve([core]));

        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(0);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Documents.Files
            .Where(file => file.Kind == ViewDocumentKind.Annotation)
            .Select(file => file.AnnotationKey!.ToString())
            .Order(StringComparer.Ordinal)
            .ShouldBe(
            [
                "cnt.42.system",
                "exe.42.scheduler.system",
                "exe.42.storyteller.system",
                "exe.42.supervisor.system",
                "rst.scheduler",
                "rst.storyteller",
                "rst.supervisor",
                "sbt.42",
                "usg.42.scheduler",
                "usg.42.storyteller",
                "usg.42.supervisor",
            ]);

        var configuration = content.Documents.Files.Single(file => file.Kind == ViewDocumentKind.Configuration);
        configuration.AnnotationKey!.ToString().ShouldBe("cnt.42.system");
        configuration.Content!.ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""{"IsSystem":true,"Number":42,"Text":"system"}""");
    }

    [Fact]
    public void Large_IsTheShapeOfTheMocksLargeDatasetInGeneratorLines()
    {
        var large = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == "large");
        var target = DataSetTarget.Resolve([large]);

        target.ShouldBe(new DataSetTarget("acme", "billing", "default"));
        large.Manifest.DependsOn.ShouldBeEmpty();

        var content = DataSetContent.Read(FileSystem, large.Folder, target);

        content.Documents.Files.ShouldBeEmpty();
        content.Documents.IgnoredFiles.ShouldBe(0);
        var lines = content.Lines.ShouldNotBeNull();
        (lines.Lines, lines.GeneratorLines, lines.Annotations.Count).ShouldBe((49, 32, 16_017));
        lines.Annotations.ShouldAllBe(line => line.ErrorCode == null);
        lines.Annotations.Select(line => line.Key!.ToString()).Distinct().Count().ShouldBe(16_017);

        // per type: 6 responsibilities and their 11 units, then per subject 2 contexts, 6 usages, 12 executions and 11
        // units of execution
        lines.Annotations
            .GroupBy(line => line.Key!.Type)
            .ToDictionary(group => group.Key, group => group.Count())
            .ShouldBe(new Dictionary<AnnotationType, int>
            {
                [AnnotationType.Responsibility] = 6,
                [AnnotationType.Unit] = 11,
                [AnnotationType.Subject] = 500,
                [AnnotationType.Context] = 1_000,
                [AnnotationType.Usage] = 3_000,
                [AnnotationType.Execution] = 6_000,
                [AnnotationType.UnitOfExecution] = 5_500,
            });

        var subjects = lines.Annotations.Where(line => line.Key!.Type == AnnotationType.Subject).Select(line => line.Key!.ToString()).ToList();
        (subjects[0], subjects[^1]).ShouldBe(("sbt.customer-001", "sbt.customer-500"));

        // every key below a subject names one of the set's responsibilities, contexts and units, so no ancestor is
        // created on the way and no parent lists more than a handful of names
        var keys = lines.Annotations.Select(line => line.Key!.ToString()).ToHashSet();
        lines.Annotations.ShouldAllBe(line => line.Key!.GetAllAncestorKeys().All(ancestor => keys.Contains(ancestor.ToString())));

        var unit = lines.Annotations.Single(line => line.Key!.ToString() == "unt.payments.sepa").Annotation.ShouldBeOfType<Unit>();
        (unit.UnitType, unit.UnitDefinition).ShouldBe(("time", "0 6 * * 1-5"));
        lines.Annotations.Single(line => line.Key!.ToString() == "sbt.customer-042").Annotation!.Values!["customerNumber"].ToString().ShouldBe("C-00042");
    }

    [Fact]
    public void Showcase_IsTheInvoicingSuiteOfTheMocksShowcase()
    {
        var showcase = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == "showcase");
        var target = DataSetTarget.Resolve([showcase]);

        target.ShouldBe(new DataSetTarget("fabrikam", "invoicing-suite", "default"));
        showcase.Manifest.DependsOn.ShouldBeEmpty();
        showcase.Manifest.ProjectDescription.ShouldNotBeNullOrWhiteSpace();

        var content = DataSetContent.Read(FileSystem, showcase.Folder, target);

        // the files a Git export of the scope gives (ui.admin's test/unit/mock/showcase-set.test.ts writes them): every
        // file of the folder is a document in the folder of its kind. The counts come from the folder, so a change of
        // the mock needs no change here.
        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(0);
        content.Documents.Files.Count.ShouldBe(DataSetFolder.GetEntries(FileSystem, showcase.Folder).Count);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Documents.Files.ShouldAllBe(file => file.Path.StartsWith(ViewArchiveNames.GetFolder(ViewArchiveNames.GetContent(file.Kind)) + "/"));
        content.Documents.Files.ShouldContain(file => file.Kind == ViewDocumentKind.Annotation);
        content.Documents.Files.ShouldContain(file => file.Kind == ViewDocumentKind.Configuration);

        // every document hangs on an annotation of the set, so the import creates no ancestor with defaults
        var keys = content.Documents.Files
            .Where(file => file.Kind == ViewDocumentKind.Annotation)
            .Select(file => file.AnnotationKey!.ToString())
            .ToHashSet();
        content.Documents.Files
            .Where(file => file.AnnotationKey is not null)
            .ShouldAllBe(file => keys.Contains(file.AnnotationKey!.ToString())
                && file.AnnotationKey!.GetAllAncestorKeys().All(ancestor => keys.Contains(ancestor.ToString())));

        // the pairs of the scope (424-265), written by the same test: both levels a seed writes, ordered by key as an
        // export writes them
        var pairs = content.KeyValues.ShouldNotBeNull();
        pairs.Levels.Select(level => level.Level).ShouldBe([KeyValueProtection.Public, KeyValueProtection.Standard]);
        pairs.Items.Select(item => item.Key).ShouldBe(pairs.Items.Select(item => item.Key).Order(StringComparer.Ordinal));

        // the project settings of the mock's project, written by the same test
        var settings = content.ProjectSettings.ShouldNotBeNull().Settings;
        settings.Types.Keys.ShouldBe(ProjectSettingsCatalog.Default.TypeCodes);
        settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Customer", Color = "blue", Icon = "i-lucide-building-2" });
        (settings.DefaultTimeZone, settings.DateFormat).ShouldBe(("Europe/Oslo", "dd.mm.yyyy"));
    }

    [Fact]
    public void Exhibit_IsTheDemoOfAcmeCorpAndGlobexWithItsTypeNames()
    {
        var exhibit = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == "exhibit");
        var target = DataSetTarget.Resolve([exhibit]);

        target.ShouldBe(new DataSetTarget("fourth-wall", "toons-and-tyrants", "default"));
        exhibit.Manifest.DependsOn.ShouldBeEmpty();

        var content = DataSetContent.Read(FileSystem, exhibit.Folder, target);

        // README.md is no document; key-values.json and project-settings.json are the set's pairs and settings
        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(1);
        content.Documents.Files.Count.ShouldBe(DataSetFolder.GetEntries(FileSystem, exhibit.Folder).Count - 1);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);

        var settings = content.ProjectSettings.ShouldNotBeNull().Settings;
        settings.Types.ToDictionary(type => type.Key, type => type.Value.Label).ShouldBe(
            new Dictionary<string, string?>
            {
                ["rst"] = "Solution",
                ["unt"] = "Job",
                ["sbt"] = "Company",
                ["usg"] = "Application",
                ["cnt"] = "Department",
                ["exe"] = "AppUsage",
                ["uxe"] = "JobInstance",
            });
        settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" });
        settings.Types.Values.ShouldAllBe(type => type.Plural != null && type.Color != null && type.Icon != null);
        (settings.DefaultTimeZone, settings.DateFormat).ShouldBe(("America/Phoenix", "yyyy-mm-dd"));
    }

    // The settings files of the sets are in the file shape that sform project settings -e writes, so a file and an
    // export compare line by line.
    [Theory]
    [InlineData("exhibit")]
    [InlineData("showcase")]
    public void ProjectSettings_AreInTheFileShape(string name)
    {
        var set = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == name);
        var settings = DataSetProjectSettings.Read(FileSystem, set.Folder).ShouldNotBeNull();

        var text = FileSystem.File.ReadAllText(settings.Path).ReplaceLineEndings("\n");

        text.ShouldBe(ProjectSettingsJson.ToJson(settings.Settings, ProjectSettingsJsonShape.File).ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
    }

    [Fact]
    public void Playground_IsASetOfItsFolder()
    {
        var folder = FileSystem.Path.Combine(RepositoryRoot(), "src", "Platform", "Storyteller", "Playground", "data");

        var playground = new DataSetCatalog(FileSystem).GetSets([folder]).Single(set => set.Source == DataSetSource.Path);

        playground.Name.ShouldBe("playground");
        playground.Manifest.DependsOn.ShouldBeEmpty();
        DataSetTarget.Resolve([playground]).ShouldBe(new DataSetTarget("42dotnet", "main", "default"));

        var content = DataSetContent.Read(FileSystem, playground.Folder, DataSetTarget.Resolve([playground]));

        content.Lines.ShouldBeNull();
        content.Documents.IgnoredFiles.ShouldBe(0);
        content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.Documents.Files.Count(file => file.Kind == ViewDocumentKind.Annotation).ShouldBe(5);
        content.Documents.Files.Count(file => file.Kind == ViewDocumentKind.Configuration).ShouldBe(5);
    }

    [Fact]
    public void Exhibit_HasThePairsOfItsConfigurations()
    {
        var exhibit = new DataSetCatalog(FileSystem).GetSets().Single(set => set.Name == "exhibit");
        var target = DataSetTarget.Resolve([exhibit]);

        target.ShouldBe(new DataSetTarget("fourth-wall", "toons-and-tyrants", "default"));

        var content = DataSetContent.Read(FileSystem, exhibit.Folder, target);

        // README.md is ignored; key-values.json and project-settings.json are the set's pairs and settings, not ignored files
        content.Documents.IgnoredFiles.ShouldBe(1);
        var pairs = content.KeyValues.ShouldNotBeNull();
        pairs.Levels.ShouldBe([(KeyValueProtection.Public, 19), (KeyValueProtection.Standard, 1)]);
        pairs.Items.Single(item => item.Protection == KeyValueProtection.Standard).Key.ShouldBe("globex.lairforce.escrowPin");
        pairs.Items.Single(item => item.Key == "acme.boomdesk.apiKeyRef").ContentType.ShouldBe(KeyValueContentTypes.SecretReference);

        // ordered by key, as an export writes them
        pairs.Items.Select(item => item.Key).ShouldBe(pairs.Items.Select(item => item.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryBuiltInSet_ReadsWithoutFailures()
    {
        var catalog = new DataSetCatalog(FileSystem);
        var sets = catalog.GetSets();

        sets.ShouldNotBeEmpty();

        foreach (var set in sets)
        {
            DataSetCatalog.Resolve(sets, [set.Name]).Last().ShouldBe(set);

            var content = DataSetContent.Read(FileSystem, set.Folder, DataSetTarget.Resolve([set], "probe", "probe"));
            content.Documents.Files.ShouldAllBe(file => file.ErrorCode == null, set.Name);
            (content.Lines?.Annotations ?? []).ShouldAllBe(line => line.ErrorCode == null, set.Name);
        }

        // the sets with project settings, which DataSetContent.Read checks by the rules
        sets.Where(set => DataSetProjectSettings.Read(FileSystem, set.Folder) is not null).Select(set => set.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["exhibit", "showcase"]);
    }

    // The import stores a binding as it is, but a resolved read of a configuration parses every binding of it and of the
    // template of its type (merged into it), and one that does not parse fails every such read.
    [Fact]
    public void EverySetOfTheRepository_HasBindingsThatParse()
    {
        // the check is the parser of the platform: a hyphen in a path, as the showcase once had, does not parse
        Should.Throw<BindingSyntaxException>(() => Parse("@(northwind.adyen-merchant, primaryVault)"));

        var playground = FileSystem.Path.Combine(RepositoryRoot(), "src", "Platform", "Storyteller", "Playground", "data");

        foreach (var set in new DataSetCatalog(FileSystem).GetSets([playground]))
        {
            var content = DataSetContent.Read(FileSystem, set.Folder, DataSetTarget.Resolve([set], "probe", "probe"));
            var documents = content.Documents.Files
                .Where(file => file.Kind is ViewDocumentKind.Configuration or ViewDocumentKind.Template && file.Content is not null);

            foreach (var document in documents)
            {
                foreach (var binding in Bindings(document.Content!))
                {
                    var text = binding.Value<string>()!;
                    var where = $"{set.Name}: {document.Path} at {binding.Path}";

                    text.Length.ShouldBeLessThanOrEqualTo(BindingExpressionLimits.MaxLength, where);
                    Should.NotThrow(() => Parse(text), where);
                }
            }
        }
    }

    // As BindingExecutor reads an '@' string, without evaluating it.
    private static BindingNode Parse(string binding)
    {
        return new Parser(new Tokenizer(binding).Tokenize()).Parse();
    }

    // The strings a resolved read binds, as ConfigurationBindingResolver walks a document: every string that starts with
    // '@', in objects and arrays at any depth, except in the $definition of an object binding (an object with a string
    // $binding), which is the program of its engine and is passed through.
    private static IEnumerable<JValue> Bindings(JObject content)
    {
        return content.Descendants()
            .OfType<JValue>()
            .Where(value => value.Type == JTokenType.String && value.Value<string>()!.StartsWith('@'))
            .Where(value => !value.Ancestors().OfType<JProperty>().Any(IsProgram));
    }

    private static bool IsProgram(JProperty property)
    {
        return property.Name == "$definition" && property.Parent is JObject envelope && envelope["$binding"]?.Type == JTokenType.String;
    }

    // The folder of 42.mono.slnx above the output of the tests.
    private static string RepositoryRoot()
    {
        var directory = FileSystem.DirectoryInfo.New(AppContext.BaseDirectory);

        while (directory is not null && !FileSystem.File.Exists(FileSystem.Path.Combine(directory.FullName, "42.mono.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No 42.mono.slnx above '{AppContext.BaseDirectory}'.");
    }
}
