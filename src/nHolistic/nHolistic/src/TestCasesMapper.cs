namespace _42.nHolistic;

public class TestCasesMapper : ITestCasesMapper
{
    private readonly Dictionary<string, HashSet<TestCaseContext>> _map = new();
    private int _count;

    public int Count => _count;

    public void RegisterTestCase(TestCaseContext testCase)
    {
        foreach (var label in testCase.Case.Labels)
        {
            if (!_map.ContainsKey(label))
            {
                _map[label] = [];
            }

            _map[label].Add(testCase);
        }

        _map[testCase.Case.FullyQualifiedName] = [testCase];
        ++_count;
    }

    public IReadOnlyCollection<TestCaseContext> GetTestCases(string label)
    {
        return _map.TryGetValue(label, out var testCases) ? testCases : [];
    }
}
