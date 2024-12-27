using _42.nHolistic.Runner.VisualStudio;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Xunit;

namespace nHolistic.Runner.VisualStudio.Tests;

public class VsTestRunnerTests
{
    [Fact]
    public void DiscoverTests()
    {
        // Arrange
        var workingFolder = Environment.CurrentDirectory;

        var sources = new List<string> { "D:\\work\\mono.me.second\\src\\nHolistic\\nHolistic.Examples\\src\\bin\\Debug\\net8.0\\42.nHolistic.Examples.dll" };
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
        var sources = new List<string> { "D:\\work\\mono.me.second\\src\\nHolistic\\nHolistic.Examples\\src\\bin\\Debug\\net8.0\\42.nHolistic.Examples.dll" };
        var runContext = new DebugRunContext();
        var frameworkHandle = new DebugFrameworkHandle();
        var vsTestRunner = new VsTestRunner();

        // Act
        vsTestRunner.RunTests(sources, runContext, frameworkHandle);
    }
}
