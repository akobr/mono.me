using _42.Platform.Storyteller.Sdk.Configuring.Flattening;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Flattening;

public class DocumentFlattenerTests
{
    [Fact]
    public void TheKeys_CompareCaseInsensitively_AsIConfigurationDoes()
    {
        var keys = DocumentFlattener.Flatten(JObject.Parse("""{ "Db": { "Host": "db.local" } }"""));

        keys["db:host"].ShouldBe("db.local");
    }

    [Fact]
    public void ADocumentReadByTheSdk_KeepsDateLikeStrings()
    {
        var document = TestDocuments.Document();

        var keys = DocumentFlattener.Flatten((JToken)document.Content);

        keys["db:renewal"].ShouldBe("2026-10-20T12:00:00.1230000+02:00");
        keys["db:port"].ShouldBe("5432");
    }

    [Fact]
    public void ANullContent_HasNoKeys()
    {
        DocumentFlattener.Flatten(null).ShouldBeEmpty();
        DocumentFlattener.Flatten(JValue.CreateNull()).ShouldBeEmpty();
    }

    [Fact]
    public void ADateTokenBuiltInCode_KeepsItsValue()
    {
        var content = new JObject { ["at"] = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(2)) };

        DocumentFlattener.Flatten(content)["at"].ShouldBe("2026-10-20T12:00:00.0000000+02:00");
    }

    [Fact]
    public void AnError_NamesThePathButNotTheValue()
    {
        var exception = Should.Throw<InvalidDocumentException>(
            () => DocumentFlattener.Flatten(JObject.Parse("""{ "Db": { "Password": "s3cret" }, "db": { "password": "0th3r" } }""")));

        exception.Message.ShouldContain("$.Db.Password");
        exception.Message.ShouldContain("$.db.password");
        exception.Message.ShouldNotContain("s3cret");
        exception.Message.ShouldNotContain("0th3r");
    }
}
