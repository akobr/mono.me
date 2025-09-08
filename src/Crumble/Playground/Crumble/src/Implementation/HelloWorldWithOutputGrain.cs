namespace _42.Crumble.Playground.Examples.Implementation;

public class HelloWorldWithOutputGrain(ICrumbExecutorFactory executorFactory) : Grain, IHelloWorldWithOutputGrain
{
    public async Task<string> ExecuteCrumb()
    {
        await using var executor = executorFactory.CreateExecutor();
        var executionContext = executor.PrepareExecutionContext(this);
        var instance = executor.CreateCrumbInstance<FirstCrumbs>();
        string output = null;

        var context = new CrumbInnerExecutionContext()
        {
            // CrumbKey = CrumbKeys.FirstCrumbs.HelloWorldWithInput,
            CrumbKey = "TODO", // generated key
            Instance = instance,
            ExecutionContext = executionContext,
            // TODO: settings can be configured globally or per crumb (attributes)
            Settings = new CrumbExecutionSetting(),
        };

        await executor.ExecuteCrumbWithMiddlewares(
            context,
            () =>
            {
                // final middleware calls the actual method
                output = instance.HelloWorldWithOutput();
                context.Output = output;
                return Task.CompletedTask;
            });

        return output;
    }

    Task ICrumbGrain.ExecuteCrumb()
    {
        return ExecuteCrumb();
    }
}
