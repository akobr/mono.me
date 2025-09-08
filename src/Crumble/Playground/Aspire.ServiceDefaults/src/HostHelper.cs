using System.Reflection;

namespace Microsoft.Extensions.Hosting;

public static class HostHelper
{
    public static bool NotInContextOfOpenApiGenerator()
    {
        return Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider";
    }
}
