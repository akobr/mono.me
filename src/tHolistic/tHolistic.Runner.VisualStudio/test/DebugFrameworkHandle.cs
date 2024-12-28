using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace tHolistic.Runner.VisualStudio.Tests;

public class DebugFrameworkHandle : IFrameworkHandle
{
    public void SendMessage(TestMessageLevel testMessageLevel, string message)
    {
        Debug.WriteLine($"[{Enum.GetName(testMessageLevel)} {DateTime.Now:hh:mm:ss.ff}] {message}");
    }

    public void RecordResult(TestResult testResult)
    {
        throw new NotImplementedException();
    }

    public void RecordStart(TestCase testCase)
    {
        throw new NotImplementedException();
    }

    public void RecordEnd(TestCase testCase, TestOutcome outcome)
    {
        throw new NotImplementedException();
    }

    public void RecordAttachments(IList<AttachmentSet> attachmentSets)
    {
        throw new NotImplementedException();
    }

    public int LaunchProcessWithDebuggerAttached(
        string filePath,
        string? workingDirectory,
        string? arguments,
        IDictionary<string, string?>? environmentVariables)
    {
        throw new NotImplementedException();
    }

    public bool EnableShutdownAfterTestRun { get; set; }
}
