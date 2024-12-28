using _42.tHolistic.Runner.VisualStudio;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Xunit;

namespace tHolistic.Runner.VisualStudio.Tests;

public class VsTestRunnerTests
{
    [Fact]
    public void DiscoverTests()
    {
        // Arrange
        var workingFolder = Environment.CurrentDirectory;

        var sources = new List<string> { "D:\\work\\mono.me.second\\src\\tHolistic\\tHolistic.Examples\\src\\bin\\Debug\\net8.0\\42.tHolistic.Examples.dll" };
        var discoveryContext = new Mock<IDiscoveryContext>();
        var logger = new DebugMessageLogger();
        var discoverySink = new DebugTestCaseDiscoverySink();

        var vsTestRunner = new VsTestRunner();

        // Act
        vsTestRunner.DiscoverTests(sources, discoveryContext.Object, logger, discoverySink);
    }

    [Fact]
    public void ExecuteTests()
    {
        // Arrange
        var sources = new List<string> { "D:\\work\\mono.me.second\\src\\tHolistic\\tHolistic.Examples\\src\\bin\\Debug\\net8.0\\42.tHolistic.Examples.dll" };
        var runContext = new DebugRunContext();
        var frameworkHandle = new DebugFrameworkHandle();
        var vsTestRunner = new VsTestRunner();

        // Act
        vsTestRunner.RunTests(sources, runContext, frameworkHandle);
    }
}
