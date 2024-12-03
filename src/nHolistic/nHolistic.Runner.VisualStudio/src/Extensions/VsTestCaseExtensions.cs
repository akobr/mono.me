using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;

namespace _42.nHolistic.Runner.VisualStudio;

public static class VsTestCaseExtensions
{
    public static TestCase ToHolisticTestCase(this VsTestCase @this)
    {
        var testCase = new TestCase
        {
            FullyQualifiedName = @this.FullyQualifiedName,
            DisplayName = @this.DisplayName,
            Source = @this.Source,
        };

        testCase.Traits.AddRange(@this.Traits.Select(trait => new TestCaseProperty { Name = trait.Name, Value = trait.Value }));

        foreach (var property in @this.Properties
                     .Where(prop => prop.Id.StartsWith($"{Constants.RunnerName}.", StringComparison.OrdinalIgnoreCase)))
        {
            testCase.Properties.Add(new TestCaseProperty
            {
                Name = property.Id[(Constants.RunnerName.Length + 1)..],
                Value = @this.GetPropertyValue(property, bool.FalseString),
            });
        }

        return testCase;
    }
}
