using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// project-settings.json of a set: read with the rules of the API, and refused as wrong input that names the file and
// the path of every problem.
public class DataSetProjectSettingsTests
{
    private static readonly string Folder = SetFiles.Folder("exhibit");
    private static readonly string File = Path.Combine(Folder, DataSetProjectSettings.FileName);

    [Fact]
    public void Read_WithoutTheFile_IsNull()
    {
        var fileSystem = SetFiles.Create(Folder, ("set.json", "{}"), ("nested/project-settings.json", "not read"));

        DataSetProjectSettings.Read(fileSystem, Folder).ShouldBeNull();
    }

    [Fact]
    public void Read_TheFileShape_IsTheNormalizedSettings()
    {
        var fileSystem = SetFiles.Create(Folder, (DataSetProjectSettings.FileName, """
            {
              // the file of sform project settings -e, with a comment and a trailing comma
              "Format": "2s.project-settings.v1",
              "Types": {
                "sbt": { "Label": " Company ", "Plural": "Companies", "Color": "Pink", "Icon": "i-lucide-building-2" },
                "uxe": { "Label": "JobInstance" },
                "cnt": {},
              },
              "DefaultTimeZone": "america/phoenix",
              "DateFormat": "YYYY-MM-DD",
            }
            """));

        var settings = DataSetProjectSettings.Read(fileSystem, Folder).ShouldNotBeNull();

        settings.Path.ShouldBe(File);
        settings.Settings.Types.Keys.ShouldBe(["sbt", "uxe"]);
        settings.Settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" });
        settings.Settings.Types["uxe"].ShouldBe(new ProjectTypeAppearance { Label = "JobInstance" });
        (settings.Settings.DefaultTimeZone, settings.Settings.DateFormat).ShouldBe(("America/Phoenix", "yyyy-mm-dd"));
        (settings.Settings.Version, settings.Settings.Modified).ShouldBe((0L, null));
    }

    [Fact]
    public void Read_TakesTheMembersAndTheTypeCodesInAnyCase()
    {
        // the admin UI's camelCase shape, which the exhibit set had before the file shape, without Format
        var fileSystem = SetFiles.Create(Folder, (DataSetProjectSettings.FileName, """
            { "types": { "SBT": { "label": "Company", "color": "pink" } }, "defaultTimeZone": "UTC", "dateformat": "d mmm yyyy" }
            """));

        var settings = DataSetProjectSettings.Read(fileSystem, Folder).ShouldNotBeNull().Settings;

        settings.Types.ShouldHaveSingleItem().Key.ShouldBe("sbt");
        settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Color = "pink" });
        (settings.DefaultTimeZone, settings.DateFormat).ShouldBe(("UTC", "d mmm yyyy"));
    }

    [Theory]
    [InlineData("""{ "Types": { "sbt": { "Color": "pinkish" } } }""", "$.Types.sbt.Color", "'pinkish' is not a color of the catalog")]
    [InlineData("""{ "DefaultTimeZone": "Central Europe Standard Time" }""", "$.DefaultTimeZone", "'Central Europe Standard Time' is not an IANA time zone id")]
    [InlineData("""{ "DateFormat": "yyyy/mm/dd" }""", "$.DateFormat", "'yyyy/mm/dd' is not a date format")]
    [InlineData("""{ "Types": {}, "Theme": "dark" }""", "$.Theme", "'Theme' is not a member")]
    [InlineData("""{ "Types": { "sbt": { "Label": "Company", "Emoji": "x" } } }""", "$.Types.sbt.Emoji", "'Emoji' is not a member")]
    [InlineData("""{ "Types": { "abc": { "Label": "Company" } } }""", "$.Types.abc", "'abc' is not a type code")]
    [InlineData("""{ "Format": "2s.project-settings.v2" }""", "$.Format", "is not 2s.project-settings.v1")]
    [InlineData("""[]""", "$", "The project settings must be a JSON object.")]
    public void Read_RefusesSettingsThatBreakARule(string content, string path, string message)
    {
        var fileSystem = SetFiles.Create(Folder, (DataSetProjectSettings.FileName, content));

        var exception = Should.Throw<DataSetException>(() => DataSetProjectSettings.Read(fileSystem, Folder));

        exception.Message.ShouldStartWith($"The project settings '{File}' are not valid (1 problem):\n  {path}: ");
        exception.Message.ShouldContain(message);
    }

    [Fact]
    public void Read_ListsEveryProblem()
    {
        var fileSystem = SetFiles.Create(Folder, (DataSetProjectSettings.FileName, """
            { "Types": { "sbt": { "Color": "pinkish", "Plural": "Companies" } }, "DefaultTimeZone": "CET-ish" }
            """));

        var exception = Should.Throw<DataSetException>(() => DataSetProjectSettings.Read(fileSystem, Folder));

        exception.Message.ShouldStartWith($"The project settings '{File}' are not valid (3 problems):");
        exception.Message.Split('\n').Skip(1).Select(line => line[..line.IndexOf(": ", StringComparison.Ordinal)].Trim())
            .ShouldBe(["$.Types.sbt.Plural", "$.Types.sbt.Color", "$.DefaultTimeZone"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("""{ "Types": { "sbt": { "Label": "Company" } """)]
    [InlineData("""{ "Types": {}, "Types": {} }""")]
    public void Read_RefusesAFileThatIsNotJson(string content)
    {
        var fileSystem = SetFiles.Create(Folder, (DataSetProjectSettings.FileName, content));

        Should.Throw<DataSetException>(() => DataSetProjectSettings.Read(fileSystem, Folder))
            .Message.ShouldStartWith($"The project settings '{File}' are not valid JSON: ");
    }

    [Fact]
    public void Read_RefusesAFileLargerThanTheLimit()
    {
        var comment = new string('x', DataSetProjectSettings.MaxFileBytes);
        var fileSystem = SetFiles.Create(Folder, (DataSetProjectSettings.FileName, $"// {comment}\n{{}}"));

        Should.Throw<DataSetException>(() => DataSetProjectSettings.Read(fileSystem, Folder))
            .Message.ShouldBe($"The project settings '{File}' cannot be read: the file is larger than 65,536 bytes.");
    }

    [Fact]
    public void Agree_IsTheSettingsWhenEverySetHasTheSame()
    {
        var first = Settings("first", """{ "Types": { "sbt": { "Label": "Company" } }, "DefaultTimeZone": "Europe/Prague" }""");
        var second = Settings("second", """{ "types": { "SBT": { "label": " Company " } }, "defaultTimeZone": "europe/prague" }""");

        DataSetProjectSettings.Agree([]).ShouldBeNull();
        DataSetProjectSettings.Agree([first]).ShouldBe(first.Settings);
        DataSetProjectSettings.Agree([first, second]).ShouldBe(first.Settings);
    }

    [Fact]
    public void Agree_RefusesSetsWithDifferentSettings()
    {
        var first = Settings("first", """{ "Types": { "sbt": { "Label": "Company" } } }""");
        var same = Settings("same", """{ "Types": { "sbt": { "Label": "Company" } } }""");
        var other = Settings("other", """{ "Types": { "sbt": { "Label": "Customer" } } }""");

        var exception = Should.Throw<DataSetException>(() => DataSetProjectSettings.Agree([first, same, other]));

        exception.Message.ShouldStartWith(
            $"The sets 'first' and 'other' carry different project settings ('{first.Settings.Path}', '{other.Settings.Path}')");
    }

    private static (DataSet Set, DataSetProjectSettings Settings) Settings(string name, string content)
    {
        var folder = SetFiles.Folder(name);
        var fileSystem = SetFiles.Create(folder, ("set.json", "{}"), (DataSetProjectSettings.FileName, content));

        return (new DataSet(DataSetManifest.Read(fileSystem, folder), folder, DataSetSource.Path), DataSetProjectSettings.Read(fileSystem, folder)!);
    }
}
