namespace _42.Testing.UI.Automation.Runtime;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
