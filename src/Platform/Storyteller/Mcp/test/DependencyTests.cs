using System.Xml.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// No MSAL in the MCP server: the token exchange replaced Entra ID on-behalf-of. Azure.Identity (the managed identity of
// the actor) brings MSAL in transitively, so the checks are the direct references and the code, not the package graph.
public class DependencyTests
{
    private static readonly string[] RemovedPackages = ["Microsoft.Identity.Client", "Microsoft.Identity.Web.TokenCache", "System.Security.Cryptography.Pkcs"];

    [Fact]
    public void ProjectFile_ReferencesNoMsalPackage()
    {
        var project = XDocument.Load(Path.Combine(FindProjectDirectory(), "Mcp.csproj"));

        var packages = project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include")).ToList();

        packages.ShouldNotContain(package => RemovedPackages.Contains(package, StringComparer.OrdinalIgnoreCase));
        packages.ShouldNotContain(package => package != null && package.StartsWith("Microsoft.Identity.", StringComparison.OrdinalIgnoreCase));
        packages.ShouldContain("Azure.Identity");
    }

    [Fact]
    public void Sources_UseNoMsalNamespace()
    {
        var directory = FindProjectDirectory();

        var users = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(directory, path))
            .Where(path => File.ReadAllText(path).Contains("Microsoft.Identity.", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(directory, path))
            .ToList();

        users.ShouldBeEmpty();
    }

    [Fact]
    public void Assembly_ReferencesNoMsalAssembly()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToList();

        references.ShouldNotContain(name => name.StartsWith("Microsoft.Identity.Client", StringComparison.OrdinalIgnoreCase));
        references.ShouldNotContain(name => name.StartsWith("Microsoft.Identity.Web", StringComparison.OrdinalIgnoreCase));
    }

    // The source directory of the MCP server, found up from the test output (the test runs from the repository).
    private static string FindProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Platform", "Storyteller", "Mcp", "src");

            if (File.Exists(Path.Combine(candidate, "Mcp.csproj")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"No src/Platform/Storyteller/Mcp/src above {AppContext.BaseDirectory}.");
    }

    private static bool IsBuildOutput(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
