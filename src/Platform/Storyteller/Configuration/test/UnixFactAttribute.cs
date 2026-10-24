namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// Unix file modes do not exist on Windows.
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "Unix file modes only.";
        }
    }
}
