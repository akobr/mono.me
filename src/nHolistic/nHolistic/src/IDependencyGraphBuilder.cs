namespace _42.nHolistic;

public interface IDependencyGraphBuilder
{
    public void RegisterTestCase(TestCase testCase);

    public void BuildGraph();
}
