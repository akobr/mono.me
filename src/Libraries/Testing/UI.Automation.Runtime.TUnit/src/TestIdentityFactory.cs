using System.Collections.Concurrent;
using System.Reflection;
using _42.Testing.UI.Automation.Runtime.Data;
using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// A TUnit test as a TestIdentity. The id is stable across runs and machines: <Assembly>/<Class>/<Method>, plus the
// display name for a data-driven case ("Data(1, one)"); TUnit's context ids are GUIDs per run. A test that runs as
// several instances (method or class arguments, [Repeat]) also gets #<hash> of TUnit's TestDetails.TestId, which holds
// the class row, the method row and the repeat index: the display name tells neither class rows nor repetitions
// apart, nor rows whose arguments format the same. The id keys the report claims, the seeds of the generated data and
// the artifact folders, so it must be unique per instance. Categories are TUnit's ([Category] and the attributes
// derived from it). The [TestCase] links of the method and its class are read by reflection: TUnit's source generator
// keeps only attributes related to TUnit types, and the engine-neutral [TestCase] is not one of them.
public static class TestIdentityFactory
{
    private static readonly ConcurrentDictionary<(Type Type, string Method), TestCaseLink[]> Cases = new();

    public static TestIdentity Create(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var details = context.Metadata.TestDetails;
        var type = details.ClassType;
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        var className = type.FullName ?? type.Name;
        var displayName = context.Metadata.DisplayName;

        return new TestIdentity(
            CreateId(assembly, className, details.MethodName, displayName, IsOneOfSeveral(details) ? details.TestId : null),
            string.IsNullOrEmpty(displayName) ? details.MethodName : displayName,
            className,
            details.MethodName,
            assembly,
            details.Categories.Distinct(StringComparer.Ordinal).ToArray(),
            FindCases(type, details.MethodName));
    }

    // instanceId: TUnit's TestId of a test that runs as several instances; null for a test that runs once.
    public static string CreateId(string assembly, string className, string methodName, string? displayName, string? instanceId = null)
    {
        var id = $"{assembly}/{className}/{methodName}";
        if (!string.IsNullOrEmpty(displayName) && !string.Equals(displayName, methodName, StringComparison.Ordinal))
        {
            id = $"{id}/{displayName}";
        }

        return string.IsNullOrEmpty(instanceId) ? id : $"{id}#{StableHash.Fnv1a32(instanceId):x8}";
    }

    // Whether TUnit builds more than one test of the method: data rows of the method or the class, or [Repeat].
    public static bool IsOneOfSeveral(TestDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        return IsOneOfSeveral(
            details.TestMethodArguments?.Length ?? 0,
            details.TestClassArguments?.Length ?? 0,
            details.GetAttributes<RepeatAttribute>().Select(repeat => repeat.Times).DefaultIfEmpty().Max());
    }

    // The [TestCase] links of every method of the name (overloads share them) and of the class and its base classes.
    public static IReadOnlyList<TestCaseLink> FindCases(Type classType, string methodName)
    {
        ArgumentNullException.ThrowIfNull(classType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);

        return Cases.GetOrAdd((classType, methodName), static key =>
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            return key.Type.GetMethods(flags)
                .Where(method => string.Equals(method.Name, key.Method, StringComparison.Ordinal))
                .SelectMany(method => method.GetCustomAttributes<TestCaseAttribute>(inherit: true))
                .Concat(key.Type.GetCustomAttributes<TestCaseAttribute>(inherit: true))
                .Select(attribute => attribute.ToLink())
                .Distinct()
                .ToArray();
        });
    }

    internal static bool IsOneOfSeveral(int methodArguments, int classArguments, int repeats)
    {
        return methodArguments > 0 || classArguments > 0 || repeats > 0;
    }
}
