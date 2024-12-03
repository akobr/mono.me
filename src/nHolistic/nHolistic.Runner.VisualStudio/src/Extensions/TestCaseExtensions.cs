using Microsoft.VisualStudio.TestPlatform.ObjectModel;

using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;

namespace _42.nHolistic.Runner.VisualStudio;

public static class TestCaseExtensions
{
    public static VsTestCase ToVsTestCase(this TestCase testCase)
    {
        var vsTestCase = new VsTestCase(testCase.FullyQualifiedName, new Uri(Constants.ExecutorUri), testCase.Source)
        {
            DisplayName = testCase.DisplayName,
        };

        vsTestCase.Traits.AddRange(
            testCase.Traits.Select(prop => new Trait(prop.Name, prop.Value)));

        foreach (var property in testCase.Properties)
        {
            var propertyId = $"{Constants.RunnerName}.{property.Name}";
            TestProperty.Register(propertyId, "RUNTIME", typeof(string), TestPropertyAttributes.None, typeof(VsTestRunner));
            var vsProperty = TestProperty.Find(propertyId)
                             ?? throw new InvalidOperationException($"Test case property {property.Name} not found.");

            vsTestCase.SetPropertyValue(vsProperty, property.Value);
        }

        return vsTestCase;
    }
}
