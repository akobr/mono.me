namespace _42.Crumble.Playground.Examples;

public class ChainedCrumbs(IChainedCrumbsExecutor executor)
{
    [Crumb]
    public Task<string> First()
    {
        return executor.Second();
    }

    [Crumb]
    public Task<string> Second()
    {
        return Task.FromResult("Hello from Second and chained crumb.");
    }
}
