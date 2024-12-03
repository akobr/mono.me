using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace nHolistic.Runner.VisualStudio.Tests;

public class DebugMessageLogger : IMessageLogger
{
    public void SendMessage(TestMessageLevel testMessageLevel, string message)
    {
        Debug.WriteLine($"[{Enum.GetName(testMessageLevel)} {DateTime.Now:hh:mm:ss.ff}] {message}");
    }
}
