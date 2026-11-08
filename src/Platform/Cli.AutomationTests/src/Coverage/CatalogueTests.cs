using System.IO.Abstractions;
using System.Reflection;
using System.Text;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// The catalogue (docs/Platform/Cli.AutomationTests/test-cases.md) against the tests of Tests/: every row has its test,
// planned in the class of its sub-folder heading under Tests/<section folder>/, with [TestCase(<Testiny id>)], the
// [Covers] of its Covers column, the role ([UserRole], Machine = [MachineOnly]), the modes (F = [FreshSignIn],
// T = [Terminal], P = [DisposableProject], X:<f> = [RequiresFeature("<f>")]; M is [MachineOnly] for the role Machine and
// a machine run otherwise, E an editor run, neither of which an attribute shows), [Elementary] for the priority Elementary,
// and the comment above it starting with the reference; and every test of Tests/ has its row. Plain TUnit tests, no
// target; skipped when the test assembly runs outside the repository (the catalogue is not at hand).
[Category(CliCategories.Coverage)]
public sealed class CatalogueTests
{
    public const string TestsNamespace = "_42.Platform.Cli.AutomationTests.Tests";

    private const string TestsFolder = "src/Platform/Cli.AutomationTests/src/Tests";

    private static readonly Lazy<Loaded> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly IReadOnlyDictionary<string, Role> RoleOf = new Dictionary<string, Role>(StringComparer.Ordinal)
    {
        ["Any"] = Role.Any,
        ["Reader"] = Role.Reader,
        ["Contributor"] = Role.Contributor,
        ["Editor"] = Role.Editor,
        ["Secrets"] = Role.Secrets,
        ["Admin"] = Role.Admin,
        ["Owner"] = Role.Owner,
    };

    // The catalogue parses, and keeps its own rules: unique references and Testiny ids, known priorities, roles, modes and
    // features, the counts of the "Cases per section" table, every P case an Owner case, every Machine case mode M.
    [Test]
    public async Task Catalogue_IsWellFormed()
    {
        var catalogue = Current().Catalogue;
        var problems = new List<string>(catalogue.Problems);

        problems.AddRange(catalogue.Cases.GroupBy(row => row.Reference).Where(group => group.Count() > 1).Select(group => $"{group.Key} is in {group.Count()} rows (lines {string.Join(", ", group.Select(row => row.Line))})."));
        problems.AddRange(catalogue.Cases.Where(row => row.TestinyId is null).Select(row => $"{row.Reference}: the Testiny id '{row.Testiny}' is not a number (line {row.Line})."));
        problems.AddRange(catalogue.Cases.Where(row => row.TestinyId is not null).GroupBy(row => row.TestinyId).Where(group => group.Count() > 1).Select(group => $"Testiny {group.Key} is the id of {string.Join(", ", group.Select(row => row.Reference))}."));
        problems.AddRange(catalogue.Cases.GroupBy(row => row.Test).Where(group => group.Count() > 1).Select(group => $"{group.Key} is the test of {string.Join(", ", group.Select(row => row.Reference))}."));
        foreach (var row in catalogue.Cases)
        {
            if (!Catalogue.Priorities.Contains(row.Priority, StringComparer.Ordinal))
            {
                problems.Add($"{row.Reference}: unknown priority '{row.Priority}' ({string.Join(", ", Catalogue.Priorities)}).");
            }

            if (!Catalogue.Roles.Contains(row.Role, StringComparer.Ordinal))
            {
                problems.Add($"{row.Reference}: unknown role '{row.Role}' ({string.Join(", ", Catalogue.Roles)}).");
            }

            foreach (var mode in row.Modes.Where(mode => !Catalogue.PlainModes.Contains(mode, StringComparer.Ordinal) && !mode.StartsWith("X:", StringComparison.Ordinal)))
            {
                problems.Add($"{row.Reference}: unknown mode '{mode}' ({string.Join(", ", Catalogue.PlainModes)}, X:<Feature>).");
            }

            foreach (var feature in row.Features.Where(feature => !CliFeatures.All.Contains(feature, StringComparer.Ordinal)))
            {
                problems.Add($"{row.Reference}: unknown feature '{feature}' ({string.Join(", ", CliFeatures.All)}).");
            }

            if (row.HasMode("P") && row.Role != "Owner")
            {
                problems.Add($"{row.Reference}: a P case acts as the Owner of its project, the row says {row.Role}.");
            }

            if (row.Role == "Machine" && !row.HasMode("M"))
            {
                problems.Add($"{row.Reference}: the role Machine needs the mode M.");
            }

            if (row.Covers.Count == 0)
            {
                problems.Add($"{row.Reference}: the Covers column is empty.");
            }
        }

        foreach (var section in catalogue.Sections)
        {
            var count = catalogue.Cases.Count(row => row.Section == section);
            if (count != section.Cases)
            {
                problems.Add($"The section {section.Name} has {count} rows, the \"Cases per section\" table says {section.Cases}.");
            }
        }

        if (problems.Count > 0)
        {
            Assert.Fail($"{Catalogue.RelativePath} has {problems.Count} problem(s):\n  {string.Join("\n  ", problems)}");
        }

        await Assert.That(catalogue.Cases.Count).IsGreaterThan(0);
    }

    // The gap: the rows whose planned test does not exist yet, with the attributes each one needs.
    [Test]
    public async Task EveryCase_IsAutomated()
    {
        var loaded = Current();
        var missing = loaded.Catalogue.Cases.Where(row => loaded.Find(row) is null).ToList();

        if (missing.Count > 0)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"{missing.Count} of {loaded.Catalogue.Cases.Count} cases of {Catalogue.RelativePath} have no test yet ({loaded.Catalogue.Cases.Count - missing.Count} automated). Each needs the method in the class below, with these attributes and [Covers] that match the Covers column (see docs/Platform/Cli.AutomationTests/automation-tests.md#the-catalogue-check):");
            foreach (var group in missing.GroupBy(row => $"{row.Section.Folder}/{row.ClassName}"))
            {
                builder.AppendLine($"{group.Key}.cs ({group.First().Section.Name} / {group.First().SubFolder})");
                foreach (var row in group)
                {
                    builder.AppendLine($"  {row.Reference} {row.Method}: {string.Join(' ', ExpectedAttributes(row))}; Covers {string.Join(", ", row.Covers)}");
                }
            }

            Assert.Fail(builder.ToString());
        }

        await Assert.That(missing.Count).IsEqualTo(0);
    }

    // Every test that exists matches its row: its place, [TestCase], role, modes, priority, [Covers] (CatalogueCovers) and
    // comment.
    [Test]
    public async Task EveryAutomatedCase_MatchesItsRow()
    {
        var loaded = Current();
        var mismatches = new List<string>();
        var automated = 0;
        foreach (var row in loaded.Catalogue.Cases)
        {
            if (loaded.Find(row) is not { } test)
            {
                continue;
            }

            automated++;
            var differences = Compare(row, test, loaded);
            if (differences.Count > 0)
            {
                mismatches.Add($"{row.Reference} ({row.Testiny}) {row.Test}:\n    {string.Join("\n    ", differences)}");
            }
        }

        if (mismatches.Count > 0)
        {
            Assert.Fail($"{mismatches.Count} of {automated} automated cases differ from their rows in {Catalogue.RelativePath}:\n  {string.Join("\n  ", mismatches)}");
        }

        await Assert.That(automated).IsGreaterThanOrEqualTo(0);
    }

    // Every test of Tests/ is planned by a row (by its class and method) and links only that row's Testiny id.
    [Test]
    public async Task EveryTest_HasACase()
    {
        var loaded = Current();
        var planned = loaded.Catalogue.Cases.ToLookup(row => $"{row.ClassFullName}.{row.Method}", StringComparer.Ordinal);
        var ids = loaded.Catalogue.Cases.Where(row => row.TestinyId is not null).Select(row => row.TestinyId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var folders = loaded.Catalogue.Sections.Select(section => section.FolderName).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var test in loaded.Tests)
        {
            var name = $"{test.Type.FullName}.{test.Method.Name}";
            var folder = test.Type.Namespace is { } ns && ns.StartsWith(TestsNamespace + ".", StringComparison.Ordinal) ? ns[(TestsNamespace.Length + 1)..] : test.Type.Namespace ?? string.Empty;
            if (!folders.Contains(folder))
            {
                problems.Add($"{Short(name)}: the folder Tests/{folder.Replace('.', '/')} is no section of the catalogue ({string.Join(", ", folders)}).");
            }

            if (!planned.Contains(name) && !loaded.Catalogue.Cases.Any(row => loaded.Find(row) is { } found && found.Method == test.Method))
            {
                problems.Add($"{Short(name)}: no row of {Catalogue.RelativePath} plans this test (the Test column of a row in the sub-folder of class {test.Type.Name}).");
            }

            foreach (var id in TestCaseIds(test.Type, test.Method).Where(id => !ids.Contains(id)))
            {
                problems.Add($"{Short(name)}: [TestCase({id})] is the Testiny id of no row.");
            }
        }

        if (problems.Count > 0)
        {
            Assert.Fail($"{problems.Count} test(s) of Tests/ are not in the catalogue:\n  {string.Join("\n  ", problems)}");
        }

        await Assert.That(loaded.Tests.Count).IsGreaterThanOrEqualTo(0);
    }

    // The attributes a row asks for, as a test writes them.
    public static IReadOnlyList<string> ExpectedAttributes(CatalogueCase row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var attributes = new List<string> { "[Test]", $"[TestCase({row.Testiny})]" };
        if (row.Priority == "Elementary")
        {
            attributes.Add("[Elementary]");
        }

        attributes.Add(row.Role == "Machine" ? "[MachineOnly]" : $"[UserRole(Role.{row.Role})]");
        if (row.HasMode("F"))
        {
            attributes.Add("[FreshSignIn]");
        }

        if (row.HasMode("T"))
        {
            attributes.Add("[Terminal]");
        }

        if (row.HasMode("P"))
        {
            attributes.Add("[DisposableProject]");
        }

        attributes.AddRange(row.Features.Select(feature => $"[RequiresFeature(CliFeatures.{feature})]"));
        return attributes;
    }

    private static List<string> Compare(CatalogueCase row, FoundTest test, Loaded loaded)
    {
        var differences = new List<string>();
        var type = test.Type;
        var method = test.Method;

        if (type.FullName != row.ClassFullName)
        {
            differences.Add($"place: the class is {type.FullName}, the row's sub-folder plans {row.ClassFullName} (Tests/{row.Section.FolderName}/{row.ClassName}.cs).");
        }

        if (method.GetCustomAttribute<TestAttribute>() is null)
        {
            differences.Add("[Test] is missing.");
        }

        var ids = TestCaseIds(type, method);
        if (ids.Count != 1 || ids[0] != row.Testiny)
        {
            differences.Add(ids.Count == 0 ? $"[TestCase({row.Testiny})] is missing." : $"[TestCase] links {string.Join(", ", ids)}, the row's Testiny id is {row.Testiny}.");
        }

        var machineOnly = Has<MachineOnlyAttribute>(type, method);
        var role = UserRoleAttribute.Of(type, method.Name);
        if (row.Role == "Machine")
        {
            if (!machineOnly)
            {
                differences.Add("role Machine: [MachineOnly] is missing.");
            }

            if (role != Role.Any)
            {
                differences.Add($"role Machine: the test has [UserRole(Role.{role})], a machine case leases no user.");
            }
        }
        else if (RoleOf.TryGetValue(row.Role, out var expected))
        {
            if (role != expected)
            {
                differences.Add($"role {row.Role}: the test acts as Role.{role}; give it [UserRole(Role.{expected})].");
            }

            if (machineOnly)
            {
                differences.Add($"role {row.Role}: [MachineOnly] leases no user; only a Machine case has it (a machine run of a user case is AsMachine).");
            }
        }

        Mode(differences, row, "F", Has<FreshSignInAttribute>(type, method), "[FreshSignIn]");
        Mode(differences, row, "T", Has<TerminalAttribute>(type, method), "[Terminal]");
        Mode(differences, row, "P", Has<DisposableProjectAttribute>(type, method), "[DisposableProject]");

        var features = type.GetCustomAttributes<RequiresFeatureAttribute>(inherit: true).Concat(method.GetCustomAttributes<RequiresFeatureAttribute>()).Select(attribute => attribute.Feature).Distinct(StringComparer.Ordinal).ToList();
        foreach (var feature in row.Features.Except(features, StringComparer.Ordinal))
        {
            differences.Add($"mode X:{feature}: [RequiresFeature(CliFeatures.{feature})] is missing.");
        }

        foreach (var feature in features.Except(row.Features, StringComparer.Ordinal))
        {
            differences.Add($"[RequiresFeature(\"{feature}\")]: the row has no mode X:{feature}.");
        }

        var elementary = Has<ElementaryAttribute>(type, method);
        if (row.Priority == "Elementary" && !elementary)
        {
            differences.Add("priority Elementary: [Elementary] is missing.");
        }
        else if (row.Priority != "Elementary" && elementary)
        {
            differences.Add($"[Elementary]: the row's priority is {row.Priority}.");
        }

        differences.AddRange(CompareCovers(row, test, loaded));

        if (loaded.Root is { } root)
        {
            var folder = loaded.FileSystem.Path.Combine(root, TestsFolder, row.Section.FolderName);
            var file = loaded.FileSystem.Path.Combine(folder, row.ClassName + ".cs");
            if (!loaded.FileSystem.File.Exists(file))
            {
                differences.Add($"place: {TestsFolder}/{row.Section.FolderName}/{row.ClassName}.cs does not exist.");
            }

            var marker = $"// {row.Reference}:";
            var sources = loaded.FileSystem.Directory.Exists(folder)
                ? loaded.FileSystem.Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                : [];
            if (!sources.Any(source => loaded.FileSystem.File.ReadAllText(source).Contains(marker, StringComparison.Ordinal)))
            {
                differences.Add($"comment: no \"{marker} {row.Title}\" above the test in {TestsFolder}/{row.Section.FolderName}/.");
            }
        }

        return differences;
    }

    // The [Covers] of the test (method and class) against the Covers column, after the coverage test's resolution
    // (CatalogueCovers has the rules).
    private static IReadOnlyList<string> CompareCovers(CatalogueCase row, FoundTest test, Loaded loaded)
    {
        if (loaded.Model is not { } model)
        {
            return [];
        }

        var usages = loaded.Usages.Where(usage => usage.TestClass == test.Type && (usage.Method is null || usage.Method == test.Method));
        return CatalogueCovers.Compare(row, [.. usages.Select(model.Resolve)]);
    }

    private static void Mode(List<string> differences, CatalogueCase row, string mode, bool has, string attribute)
    {
        if (row.HasMode(mode) && !has)
        {
            differences.Add($"mode {mode}: {attribute} is missing.");
        }
        else if (!row.HasMode(mode) && has)
        {
            differences.Add($"{attribute}: the row has no mode {mode}.");
        }
    }

    private static bool Has<TAttribute>(Type type, MethodInfo method)
        where TAttribute : Attribute
    {
        return method.GetCustomAttribute<TAttribute>() is not null || type.GetCustomAttribute<TAttribute>(inherit: true) is not null;
    }

    private static IReadOnlyList<string> TestCaseIds(Type type, MethodInfo method)
    {
        return [.. method.GetCustomAttributes<TestCaseAttribute>().Concat(type.GetCustomAttributes<TestCaseAttribute>(inherit: true)).Select(attribute => attribute.Id).Distinct(StringComparer.Ordinal)];
    }

    private static string Short(string name)
    {
        return name.StartsWith(TestsNamespace + ".", StringComparison.Ordinal) ? "Tests." + name[(TestsNamespace.Length + 1)..] : name;
    }

    private static Loaded Current()
    {
        if (Shared.Value.SkipReason is { } reason)
        {
            Skip.Test(reason);
        }

        return Shared.Value;
    }

    private static Loaded Load()
    {
        var fileSystem = new FileSystem();
        var root = RepositoryRoot.Find(fileSystem);
        if (root is null)
        {
            return Loaded.Skipped($"The test assembly runs outside the repository (no {RepositoryRoot.SolutionFile} above {AppContext.BaseDirectory}), so {Catalogue.RelativePath} is not at hand.");
        }

        var path = fileSystem.Path.Combine(root, Catalogue.RelativePath);
        if (!fileSystem.File.Exists(path))
        {
            return Loaded.Skipped($"{path} does not exist.");
        }

        var catalogue = Catalogue.Parse(fileSystem.File.ReadAllText(path));
        var model = CoverageContext.Unavailable() is null ? CoverageContext.Current.Model : null;
        var usages = model is null ? CoversUsage.Read(typeof(CatalogueTests).Assembly) : CoverageContext.Current.Usages;
        var tests = typeof(CatalogueTests).Assembly.GetTypes()
            .Where(type => type.Namespace is { } ns && ns.StartsWith(TestsNamespace + ".", StringComparison.Ordinal) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<TestAttribute>() is not null)
                .Select(method => new FoundTest(type, method)))
            .ToList();
        return new Loaded(null, root, catalogue, model, usages, tests, fileSystem);
    }

    private sealed record FoundTest(Type Type, MethodInfo Method);

    private sealed record Loaded(string? SkipReason, string? Root, Catalogue Catalogue, CoverageModel? Model, IReadOnlyList<CoversUsage> Usages, IReadOnlyList<FoundTest> Tests, IFileSystem FileSystem)
    {
        public static Loaded Skipped(string reason) => new(reason, null, Catalogue.Parse(string.Empty), null, [], [], new FileSystem());

        // The test of a row: the method in the planned class, else in a class of that name elsewhere under Tests/ (a
        // misplaced class is a mismatch, not a missing test).
        public FoundTest? Find(CatalogueCase row)
        {
            return Tests.FirstOrDefault(test => test.Type.FullName == row.ClassFullName && test.Method.Name == row.Method)
                ?? Tests.FirstOrDefault(test => test.Type.Name == row.ClassName && test.Method.Name == row.Method);
        }
    }
}
