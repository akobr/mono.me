namespace _42.Crumble.Playground.Examples.Implementation;

[global::Orleans.Concurrency.StatelessWorker]
public class HelloWorldGrain(ICrumbExecutorFactory executorFactory) : Grain, IHelloWordGrain
{
    public async Task ExecuteCrumb()
    {
        await using var executor = executorFactory.CreateExecutor();
        var executionContext = executor.PrepareExecutionContext(this);
        var instance = executor.CreateCrumbInstance<SyncCrumbs>();

        var context = new CrumbInnerExecutionContext()
        {
            CrumbKey = "TODO", // generated key
            Instance = instance,
            ExecutionContext = executionContext,
            Settings = new CrumbExecutionSetting(),
        };

        await executor.ExecuteCrumbWithMiddlewares(
            context,
            async () => // async only with output
            {
                // final middleware calls the actual method
                instance.HelloWorld();
                // TODO: output
                // var output = await instance.CrumbWithOutput();
                // context.Output = "Hello, World!";
            });
    }
}
