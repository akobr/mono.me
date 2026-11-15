using System.IO.Abstractions.TestingHelpers;
using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// key-values.json of a set: the format of the key/value export and import, and the refusals of a seed, all before
// anything is written.
public class DataSetKeyValuesTests
{
    private static readonly string Folder = SetFiles.Folder("pairs");

    [Fact]
    public void Read_WithoutTheFile_IsNull()
    {
        DataSetKeyValues.Read(SetFiles.Create(Folder, ("set.json", "{}")), Folder).ShouldBeNull();
    }

    [Fact]
    public void Read_TakesTheDocumentOfAnExport()
    {
        var pairs = Read("""
            {
              "Format": "2s.key-values.v1",
              "Items": [
                { "Key": "smtp.host", "Protection": "Public", "Value": "smtp.fabrikam.example", "ContentType": "text/plain" },
                { "Key": "features.flags", "Protection": "Public", "Value": "{\"ksef\":true}", "ContentType": "application/json" },
                { "Key": "smtp.password", "Protection": "Standard", "Value": "demo-only-value" },
                { "Key": "vault.peppolKey", "Protection": "Public", "Value": "kv://fabrikam-prod/peppol-key", "ContentType": "application/vnd.2s.secret-reference" }
              ]
            }
            """).ShouldNotBeNull();

        pairs.Items.Select(item => (item.Key, item.Protection, item.Value, item.ContentType)).ShouldBe(
        [
            ("smtp.host", KeyValueProtection.Public, "smtp.fabrikam.example", "text/plain"),
            ("features.flags", KeyValueProtection.Public, """{"ksef":true}""", "application/json"),
            ("smtp.password", KeyValueProtection.Standard, "demo-only-value", null),
            ("vault.peppolKey", KeyValueProtection.Public, "kv://fabrikam-prod/peppol-key", "application/vnd.2s.secret-reference"),
        ]);
        pairs.Levels.ShouldBe([(KeyValueProtection.Public, 3), (KeyValueProtection.Standard, 1)]);
        (pairs.Count(KeyValueProtection.Public), pairs.Count(KeyValueProtection.Standard), pairs.Count(KeyValueProtection.Hsm)).ShouldBe((3, 1, 0));
    }

    [Fact]
    public void Read_TakesMembersAndLevelsInAnyCase_CommentsTrailingCommasAndNoFormat()
    {
        var pairs = Read("""
            // the format is the current one when it is missing
            {
              "items": [
                { "key": "a", "protection": "public", "value": "1", },
                { "KEY": "b", "PROTECTION": "STANDARD", "VALUE": "2" },
              ],
            }
            """).ShouldNotBeNull();

        pairs.Items.Select(item => (item.Key, item.Protection)).ShouldBe([("a", KeyValueProtection.Public), ("b", KeyValueProtection.Standard)]);
        Read("""{ "Items": [] }""").ShouldNotBeNull().Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json", "are not valid")]
    [InlineData("[]", "are not valid")]
    [InlineData("null", "it is not a JSON object")]
    [InlineData("""{ "Format": "2s.key-values.v2", "Items": [] }""", "the format '2s.key-values.v2' is not supported")]
    [InlineData("""{ "Format": "2s.key-values.v1" }""", "missing required properties")]
    [InlineData("""{ "Items": null }""", "Items is not an array of pairs")]
    [InlineData("""{ "Items": [], "Version": 1 }""", "Version")]
    [InlineData("""{ "Items": [ { "Key": "a", "Protection": "Public", "Value": "1", "Level": "x" } ] }""", "Level")]
    [InlineData("""{ "Items": [ { "Key": "a", "Value": "1" } ] }""", "missing required properties")]
    [InlineData("""{ "Items": [ { "Key": "a", "Protection": 1, "Value": "1" } ] }""", "are not valid")]
    [InlineData("""{ "Items": [ { "Key": "a", "Protection": "Premium", "Value": "1" } ] }""", "are not valid")]
    [InlineData("""{ "Items": [ { "Key": "a", "Protection": "Public", "Value": 5 } ] }""", "are not valid")]
    public void Read_RefusesAFileThatIsNotADocumentOfPairs(string content, string message)
    {
        Should.Throw<DataSetException>(() => Read(content)).Message.ShouldContain(message);
    }

    [Theory]
    [InlineData("""{ "Key": "smtp-host", "Protection": "Public", "Value": "x" }""", "item 1 'smtp-host': The key 'smtp-host' is not valid")]
    [InlineData("""{ "Key": null, "Protection": "Public", "Value": "x" }""", "item 1 '': The key '' is not valid")]
    [InlineData("""{ "Key": "api.token", "Protection": "Hsm", "Value": "x" }""", "item 1 'api.token': the level Hsm cannot be seeded")]
    [InlineData("""{ "Key": "api.token", "Protection": "Byok", "Value": "x" }""", "the level Byok cannot be seeded")]
    [InlineData("""{ "Key": "api.token", "Protection": "Hyok", "Value": "x" }""", "the level Hyok cannot be seeded")]
    [InlineData("""{ "Key": "db.password", "Protection": "Standard" }""", "item 1 'db.password': it has no Value")]
    [InlineData("""{ "Key": "db.host", "Protection": "Public" }""", "it has no Value")]
    [InlineData("""{ "Key": "ref", "Protection": "Standard", "Value": "kv://v/s", "ContentType": "application/vnd.2s.secret-reference" }""", "a secret reference is a Public pair")]
    [InlineData("""{ "Key": "ref", "Protection": "Public", "Value": "not a reference", "ContentType": "application/vnd.2s.secret-reference" }""", "The secret reference is not valid")]
    public void Read_RefusesAnItemASeedCannotWrite(string item, string message)
    {
        var exception = Should.Throw<DataSetException>(() => Read($$"""{ "Items": [ {{item}} ] }"""));

        exception.Message.ShouldContain("cannot be seeded: 1 item is not valid; nothing was written.");
        exception.Message.ShouldContain(message);
    }

    [Fact]
    public void Read_RefusesValuesAndContentTypesBeyondTheRulesOfTheImport()
    {
        var value = new string('x', 24_577);
        var contentType = new string('t', 256);

        Should.Throw<DataSetException>(() => Read($$"""{ "Items": [ { "Key": "a", "Protection": "Public", "Value": "{{value}}" } ] }"""))
            .Message.ShouldContain("A value may have at most 24576 bytes in UTF-8.");
        Should.Throw<DataSetException>(() => Read($$"""{ "Items": [ { "Key": "a", "Protection": "Public", "Value": "1", "ContentType": "{{contentType}}" } ] }"""))
            .Message.ShouldContain("A content type may have at most 255 characters.");

        // the largest of each is fine
        Read($$"""{ "Items": [ { "Key": "a", "Protection": "Public", "Value": "{{value[1..]}}", "ContentType": "{{contentType[1..]}}" } ] }""").ShouldNotBeNull();
    }

    [Fact]
    public void Read_RefusesAKeyListedTwiceIgnoringCase()
    {
        var exception = Should.Throw<DataSetException>(() => Read("""
            { "Items": [
              { "Key": "smtp.host", "Protection": "Public", "Value": "a" },
              { "Key": "smtp.port", "Protection": "Public", "Value": "25" },
              { "Key": "SMTP.Host", "Protection": "Standard", "Value": "b" }
            ] }
            """));

        exception.Message.ShouldContain("item 3 'SMTP.Host': the key is listed more than once, ignoring case (first as item 1)");
    }

    [Fact]
    public void Read_ListsEveryProblemUpToTwenty()
    {
        var items = string.Join(",", Enumerable.Range(1, 25).Select(index => $$"""{ "Key": "bad-{{index}}", "Protection": "Public", "Value": "x" }"""));
        var exception = Should.Throw<DataSetException>(() => Read($$"""{ "Items": [ { "Key": "good", "Protection": "Public", "Value": "x" }, {{items}}, null ] }"""));

        var lines = exception.Message.Split(Environment.NewLine);
        lines[0].ShouldEndWith("cannot be seeded: 26 items are not valid; nothing was written.");
        lines[1].ShouldBe("  item 2 'bad-1': The key 'bad-1' is not valid: use segments of letters, digits and '_' that start with a letter or '_', joined by '.', at most 128 characters.");
        lines.Length.ShouldBe(22);
        lines[^1].ShouldBe("  … and 6 more");

        // a null item is one problem of its own
        Should.Throw<DataSetException>(() => Read("""{ "Items": [ null ] }""")).Message.ShouldContain("item 1: it is not a pair");
    }

    [Fact]
    public void Read_RefusesMoreThanTheMostPairsOfASet()
    {
        var items = string.Join(",", Enumerable.Range(1, DataSetKeyValues.MaxPairs + 1).Select(index => $$"""{"Key":"k{{index}}","Protection":"Public","Value":"v"}"""));

        Should.Throw<DataSetException>(() => Read($$"""{ "Items": [ {{items}} ] }"""))
            .Message.ShouldContain("it has 10,001 pairs, and a set holds at most 10,000");
    }

    [Fact]
    public void Read_RefusesAFileLargerThanTheLimitBeforeItIsRead()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(Folder, DataSetKeyValues.FileName), new MockFileData(new byte[DataSetKeyValues.MaxFileBytes + 1]));

        Should.Throw<DataSetException>(() => DataSetKeyValues.Read(fileSystem, Folder))
            .Message.ShouldContain("the file is larger than 33,554,432 bytes");
    }

    private static DataSetKeyValues? Read(string content)
    {
        return DataSetKeyValues.Read(SetFiles.Create(Folder, ("set.json", "{}"), (DataSetKeyValues.FileName, content)), Folder);
    }
}
