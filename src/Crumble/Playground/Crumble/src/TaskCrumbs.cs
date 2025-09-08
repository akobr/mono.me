namespace _42.Crumble.Playground.Examples;

public class TaskCrumbs
{
    [Crumb]
    public Task SimpleTask() => Task.CompletedTask;

    [Crumb]
    public Task SimpleTaskWithInput(int input) => Task.FromResult(input);

    [Crumb]
    public Task<string> SimpleTaskWithOutput() => Task.FromResult("Hello, World!");

    [Crumb]
    public Task<string> SimpleTaskWithInputAndOutput(string input) => Task.FromResult(input);
}
