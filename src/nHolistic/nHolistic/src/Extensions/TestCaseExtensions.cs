using System.Diagnostics;
using System.Reflection;

namespace _42.nHolistic;

public static class TestCaseExtensions
{
    public static Assembly GetAssembly(this TestCase testCase)
    {
        var assemblyFileName = Path.GetFullPath(testCase.Source);
        var assembly = Assembly.LoadFrom(assemblyFileName);
        return assembly;
    }

    public static Type GetTestType(this TestCase testCase)
    {
        var assembly = testCase.GetAssembly();
        var type = assembly.GetType(testCase.TypeFullyQualifiedName);
        return type ?? throw new InvalidOperationException(
            $"Type '{testCase.TypeFullyQualifiedName}' not found in assembly '{assembly.FullName}'.");
    }

    public static TestAttribute GetTestAttribute(this TestCase testCase)
    {
        var type = testCase.GetTestType();

        if (testCase.MethodName is null)
        {
            var classAttribute = type.GetCustomAttribute<TestAttribute>();
            return classAttribute ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' does not have a [Test] attribute.");
        }

        var method = type.GetMethod(
            testCase.MethodName,
            BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        if (method is null)
        {
            throw new InvalidOperationException(
                $"Method '{testCase.MethodName}' not found in type '{type.FullName}'.");
        }

        var methodAttribute = method.GetCustomAttribute<TestAttribute>();
        return methodAttribute ?? throw new InvalidOperationException(
            $"Method '{testCase.MethodName}' in type '{type.FullName}' does not have a [Test] attribute.");
    }

    public static TestCaseContext CreateContext(this TestCase testCase)
    {
        var assembly = Assembly.LoadFrom(testCase.Source);
        var targetType = assembly.GetType(testCase.TypeFullyQualifiedName)
                     ?? throw new InvalidOperationException(
                         $"Type '{testCase.TypeFullyQualifiedName}' not found in assembly '{assembly.FullName}'.");
        TestAttribute attribute;

        if (testCase.MethodName is null)
        {
            attribute = targetType.GetCustomAttribute<TestAttribute>()
                        ?? throw new InvalidOperationException(
                            $"Type '{targetType.FullName}' does not have a [Test] attribute.");

            return new TestCaseContext
            {
                Case = testCase,
                Assembly = assembly,
                Attribute = attribute,
                TargetType = targetType,
            };
        }

        var targetMethod = targetType.GetMethod(
                           testCase.MethodName,
                           BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                       ?? throw new InvalidOperationException(
                           $"Method '{testCase.MethodName}' not found in type '{targetType.FullName}'.");

        attribute = targetMethod.GetCustomAttribute<TestAttribute>()
                    ?? throw new InvalidOperationException(
                        $"Method '{testCase.MethodName}' in type '{targetType.FullName}' does not have a [Test] attribute.");

        return new TestCaseContext
        {
            Case = testCase,
            Assembly = assembly,
            Attribute = attribute,
            TargetType = targetType,
            TargetMethod = targetMethod,
        };
    }
}
