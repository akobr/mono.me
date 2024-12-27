using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace nHolistic.Runner.VisualStudio.Tests;

public class DebugRunContext : IRunContext
{
    public IRunSettings? RunSettings { get; }

    public ITestCaseFilterExpression? GetTestCaseFilter(IEnumerable<string>? supportedProperties, Func<string, TestProperty?> propertyProvider)
    {
        throw new NotImplementedException();
    }

    public bool KeepAlive { get; }

    public bool InIsolation { get; }

    public bool IsDataCollectionEnabled { get; }

    public bool IsBeingDebugged { get; }

    public string? TestRunDirectory { get; }

    public string? SolutionDirectory { get; }
}
