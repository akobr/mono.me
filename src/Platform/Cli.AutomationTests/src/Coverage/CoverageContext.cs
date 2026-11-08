using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// What CommandCoverageTests and CatalogueTests share, read once per test process: the command tree of the build of
// src/Platform/Cli/src (the path MSBuild records as SformPath; Cli:Path is not used, as the coverage is that of the source),
// coverage-values.json, coverage-exceptions.json and every [Covers] of the test assembly.
public sealed class CoverageContext
{
    private static readonly Lazy<CoverageContext> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private CoverageContext(CoverageModel model, CoverageExceptions exceptions, IReadOnlyList<CoversUsage> usages)
    {
        Model = model;
        Exceptions = exceptions;
        Usages = usages;
    }

    public static CoverageContext Current => Shared.Value;

    public CoverageModel Model { get; }

    public CommandTree Tree => Model.Tree;

    public CoverageExceptions Exceptions { get; }

    public IReadOnlyList<CoversUsage> Usages { get; }

    // Why the context cannot be read here, or null: the CLI was not built next to the tests (e.g. the test assembly runs
    // from a copy). Then the coverage tests skip with it.
    public static string? Unavailable()
    {
        var path = SformLocator.RecordedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "The test assembly records no path of 42.Platform.Cli.dll (SformPath); build the tests with dotnet build, which builds src/Platform/Cli/src too.";
        }

        return new System.IO.Abstractions.FileSystem().File.Exists(path)
            ? null
            : $"42.Platform.Cli.dll is not at the recorded '{path}' (the tests run from a copy?); the coverage needs the build of src/Platform/Cli/src.";
    }

    private static CoverageContext Load()
    {
        var values = CoverageValues.Load();
        var enums = values.Options.Select(entry => entry.Enum).OfType<string>();
        var tree = CommandTreeReader.Read(SformLocator.RecordedPath!, enums);
        return new CoverageContext(new CoverageModel(tree, values), CoverageExceptions.Load(), CoversUsage.Read(typeof(CoverageContext).Assembly));
    }
}
