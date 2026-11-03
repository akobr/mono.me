using System.Runtime.CompilerServices;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// Unix file modes do not exist on Windows.
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix file modes only.";
        }
    }
}
