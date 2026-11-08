using System.Reflection;
using TUnit.Core;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// One [Covers] of the test assembly: on a test method, or on a class (then for each of its tests). Every test counts,
// skipped or not; Features and IsSkipped tell whether it can run against every target (coverage-exceptions.json).
public sealed record CoversUsage(Type TestClass, MethodInfo? Method, CoversAttribute Covers, bool IsTest, IReadOnlyList<string> Features, bool IsSkipped)
{
    // Needs a feature of the target ([RequiresFeature]) or never runs ([Skip]).
    public bool IsGated => Features.Count > 0 || IsSkipped;

    // "Tests.KeyValues.KeyValuePairTests.Kv_SetGetPublic_RoundTrip" (the namespace below the assembly's root).
    public string TestName
    {
        get
        {
            var type = TestClass.FullName ?? TestClass.Name;
            var root = typeof(CoversUsage).Namespace![..typeof(CoversUsage).Namespace!.LastIndexOf('.')] + ".";
            var name = type.StartsWith(root, StringComparison.Ordinal) ? type[root.Length..] : type;
            return Method is null ? name : $"{name}.{Method.Name}";
        }
    }

    // How the attribute is written, for messages: [Covers("story kv set", "arg0", "--secret")].
    public string Written => $"[Covers({string.Join(", ", new[] { Covers.Command }.Concat(Covers.Items).Select(item => $"\"{item}\""))})]";

    // Every [Covers] of the assembly's types and methods, in a stable order.
    public static IReadOnlyList<CoversUsage> Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var usages = new List<CoversUsage>();
        foreach (var type in assembly.GetTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var classFeatures = type.GetCustomAttributes<RequiresFeatureAttribute>(inherit: true).Select(attribute => attribute.Feature).ToList();
            var classSkipped = type.GetCustomAttributes<SkipAttribute>(inherit: true).Any();
            foreach (var covers in type.GetCustomAttributes<CoversAttribute>(inherit: false))
            {
                usages.Add(new CoversUsage(type, null, covers, IsTest: true, classFeatures, classSkipped));
            }

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .OrderBy(method => method.Name, StringComparer.Ordinal);
            foreach (var method in methods)
            {
                var all = method.GetCustomAttributes<CoversAttribute>(inherit: false).ToList();
                if (all.Count == 0)
                {
                    continue;
                }

                var features = classFeatures.Concat(method.GetCustomAttributes<RequiresFeatureAttribute>().Select(attribute => attribute.Feature)).Distinct(StringComparer.Ordinal).ToList();
                var skipped = classSkipped || method.GetCustomAttributes<SkipAttribute>().Any();
                var isTest = method.GetCustomAttribute<TestAttribute>() is not null;
                usages.AddRange(all.Select(covers => new CoversUsage(type, method, covers, isTest, features, skipped)));
            }
        }

        return usages;
    }
}
