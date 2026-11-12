using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetManifestTests
{
    [Fact]
    public void Read_TakesTheMembersInAnyCase()
    {
        var folder = SetFiles.Folder("showcase-folder");
        var fileSystem = SetFiles.Create(folder, ("set.json", """
            {
              // a comment, and a trailing comma
              "name": "showcase",
              "VERSION": 3,
              "description": "The showcase.",
              "organization": "fabrikam",
              "PROJECT": "invoicing-suite",
              "view": "preview",
              "projectDescription": "Invoices.",
              "viewdescription": "A preview.",
              "dependsOn": [ "core", " large " ],
            }
            """));

        var manifest = DataSetManifest.Read(fileSystem, folder);

        manifest.Name.ShouldBe("showcase");
        manifest.Version.ShouldBe(3);
        manifest.Description.ShouldBe("The showcase.");
        manifest.Organization.ShouldBe("fabrikam");
        manifest.Project.ShouldBe("invoicing-suite");
        manifest.View.ShouldBe("preview");
        manifest.ProjectDescription.ShouldBe("Invoices.");
        manifest.ViewDescription.ShouldBe("A preview.");
        manifest.DependsOn.ShouldBe(["core", "large"]);
    }

    [Fact]
    public void Read_DefaultsTheNameToTheFolderAndTheVersionTo1()
    {
        var folder = SetFiles.Folder("my-set");
        var fileSystem = SetFiles.Create(folder, ("set.json", "{}"));

        var manifest = DataSetManifest.Read(fileSystem, folder + Path.DirectorySeparatorChar);

        manifest.Name.ShouldBe("my-set");
        manifest.Version.ShouldBe(1);
        manifest.DependsOn.ShouldBeEmpty();
        manifest.Organization.ShouldBeNull();
        manifest.Project.ShouldBeNull();
        manifest.View.ShouldBeNull();
    }

    [Fact]
    public void Read_RefusesAnUnknownMember()
    {
        var folder = SetFiles.Folder("typo");
        var fileSystem = SetFiles.Create(folder, ("set.json", """{ "Nmae": "typo" }"""));

        Should.Throw<DataSetException>(() => DataSetManifest.Read(fileSystem, folder)).Message.ShouldContain("Nmae");
    }

    [Theory]
    [InlineData("""{ "Name": "My Set" }""", "'My Set'")]
    [InlineData("""{ "Name": "a" }""", "'a'")]
    [InlineData("""{ "Version": 0 }""", "Version must be 1 or more")]
    [InlineData("""{ "Version": "2" }""", "is not valid")]
    [InlineData("""{ "DependsOn": [ "core", "" ] }""", "DependsOn has an empty name")]
    [InlineData("""[ "core" ]""", "is not valid")]
    [InlineData("""{ "Name": "core" """, "is not valid")]
    public void Read_RefusesAManifestThatIsNotValid(string content, string message)
    {
        var folder = SetFiles.Folder("broken");
        var fileSystem = SetFiles.Create(folder, ("set.json", content));

        Should.Throw<DataSetException>(() => DataSetManifest.Read(fileSystem, folder)).Message.ShouldContain(message);
    }

    [Fact]
    public void Read_RefusesAFolderNameThatIsNotASetName()
    {
        var folder = SetFiles.Folder("My Set");
        var fileSystem = SetFiles.Create(folder, ("set.json", "{}"));

        Should.Throw<DataSetException>(() => DataSetManifest.Read(fileSystem, folder)).Message.ShouldContain("'My Set'");
    }

    [Fact]
    public void Read_RefusesAFolderWithoutAManifest()
    {
        var folder = SetFiles.Folder("empty");
        var fileSystem = SetFiles.Create(folder, ("annotations/sbt.a.json", "{}"));

        Should.Throw<DataSetException>(() => DataSetManifest.Read(fileSystem, folder)).Message.ShouldContain("has no set.json");
    }
}
