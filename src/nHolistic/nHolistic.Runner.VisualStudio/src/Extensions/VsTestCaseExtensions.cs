using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;

namespace _42.nHolistic.Runner.VisualStudio;

public static class VsTestCaseExtensions
{
    public static TestCase ToHolisticTestCase(this VsTestCase @this)
    {
        var properties = new Dictionary<string, TestCaseProperty>();

        foreach (var property in @this.Properties
                     .Where(prop => prop.Id.StartsWith($"{Constants.RunnerName}.", StringComparison.OrdinalIgnoreCase)))
        {
            var name = property.Id[(Constants.RunnerName.Length + 1)..];
            properties.Add(
                name,
                new TestCaseProperty { Name = name, Value = @this.GetPropertyValue(property, bool.FalseString), });
        }

        var testCase = new TestCase
        {
            FullyQualifiedName = @this.FullyQualifiedName,
            TypeFullyQualifiedName = properties.TryGetValue(nameof(TestCase.TypeFullyQualifiedName), out var typeNameProperty)
                ? typeNameProperty.Value
                : throw new InvalidDataException($"Required property '{nameof(TestCase.TypeFullyQualifiedName)}' is missing on the test case '{@this.FullyQualifiedName}' passed from Visual Studio."),
            MethodName = properties.TryGetValue(nameof(TestCase.MethodName), out var methodNameProperty)
                ? methodNameProperty.Value
                : null,
            DisplayName = @this.DisplayName,
            Source = @this.Source,
        };

        testCase.Properties.AddRange(properties.Values);
        testCase.Traits.AddRange(@this.Traits.Select(trait => new TestCaseProperty { Name = trait.Name, Value = trait.Value }));
        return testCase;
    }
}
