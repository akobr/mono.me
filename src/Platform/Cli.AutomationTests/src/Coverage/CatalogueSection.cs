namespace _42.Platform.Cli.AutomationTests.Coverage;

// A section of the catalogue: a Testiny top-level folder and its folder of Tests/ ("Tests/SignIn").
public sealed record CatalogueSection(string Name, string Folder, int Cases)
{
    // The folder below Tests/, also the namespace below _42.Platform.Cli.AutomationTests.Tests ("SignIn").
    public string FolderName => Folder.StartsWith("Tests/", StringComparison.Ordinal) ? Folder["Tests/".Length..] : Folder;
}
