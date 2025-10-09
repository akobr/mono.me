namespace _42.Crumble.Playground.Examples.Implementation;

public class HelloWorldWithInputGrain(ICrumbExecutorFactory executorFactory) : Grain, IHelloWorldWithInputGrain
{
    public async Task ExecuteCrumb(string input)
    {
        await using var executor = executorFactory.CreateExecutor();
        var executionContext = executor.PrepareExecutionContext(this);
        var instance = executor.CreateCrumbInstance<SyncCrumbs>();

        var context = new CrumbInnerExecutionContext()
        {
            CrumbKey = "TODO", // generated key
            Instance = instance,
            Input = input,
            // TODO: settings can be configured globally or per crumb (attributes)
            ExecutionContext = executionContext,
            Settings = new CrumbExecutionSetting(),
        };

        await executor.ExecuteCrumbWithMiddlewares(
            context,
            () =>
            {
                instance.HelloWorldWithInput(input);
                return Task.CompletedTask;
            });
    }
}
