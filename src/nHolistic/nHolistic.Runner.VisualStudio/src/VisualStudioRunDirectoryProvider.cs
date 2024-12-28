using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace _42.tHolistic.Runner.VisualStudio;

public class VisualStudioRunDirectoryProvider(IRunContext runContext) : IRunDirectoryProvider
{
    public string GetRunDirectory()
    {
        return runContext.TestRunDirectory ?? Environment.CurrentDirectory;
    }
}
