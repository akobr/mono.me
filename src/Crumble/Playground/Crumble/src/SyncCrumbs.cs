namespace _42.Crumble.Playground.Examples;

public class SyncCrumbs
{
    private readonly IServiceProvider _services;

    public SyncCrumbs(IServiceProvider services)
    {
        this._services = services;
    }

    [Crumb(IsSingleAndSynchronized = true)]
    public void HelloWorld()
    {
        Console.WriteLine("Hello, World!");
    }

    [Crumb]
    public string HelloWorldWithOutput()
    {
        Console.WriteLine("Hello, World! As output.");
        return "Hello, World!";
    }

    [Crumb]
    public void HelloWorldWithInput(string input)
    {
        Console.WriteLine($"Hello, {input}!");
    }

    [Crumb]
    public string HelloWorldWithInputAndOutput(string input)
    {
        Console.WriteLine($"Hello, {input}!");
        return $"Hello, {input}!";
    }
}
