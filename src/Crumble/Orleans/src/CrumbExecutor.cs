using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Crumble;

public class CrumbExecutor(AsyncServiceScope scope) : ICrumbExecutor
{
    private readonly AsyncServiceScope _scope = scope;
    private readonly IServiceProvider _services = scope.ServiceProvider;

    public ValueTask DisposeAsync()
    {
        return _scope.DisposeAsync();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    public ICrumbExecutionContext PrepareExecutionContext(Grain crumbGrain)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("crumble.crumb.context-creation");
        var contextKey = crumbGrain.GetPrimaryKeyString();
        // TODO: [P2] work with flags and properties in a better way if possible
        var context = OrleansCrumbExecutionContext.FromRequestContext();
        context.ContextKey = contextKey;
        return context;
    }

    public TCrumbInstance CreateCrumbInstance<TCrumbInstance>()
    {
        using var activity = Telemetry.ActivitySource.StartActivity("crumble.crumb.instantiation");
        return ActivatorUtilities.GetServiceOrCreateInstance<TCrumbInstance>(_services);
    }

    public async Task ExecuteCrumbWithMiddlewares(ICrumbInnerExecutionContext context, Func<Task> crumbAction)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("crumble.crumb.execution");
        var stopwatch = new PhaseStopwatch();
        var middlewaresProvider = _services.GetRequiredService<IMiddlewaresProvider>();
        using var logger = _services.GetRequiredService<ICrumbLogger>();
        Exception? crumbException = null;

        var middlewaresChain = middlewaresProvider.GetMiddlewareFullChain(async ctx =>
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                logger.LogBeforeCrumbExecution(ctx);
                await crumbAction();
                Telemetry.Crumbs.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
            }
            catch (Exception exception)
            {
                Telemetry.Crumbs.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
                logger.LogException(exception, ctx);
                crumbException = exception;
                throw;
            }
            finally
            {
                Telemetry.Crumbs.Add(1, new KeyValuePair<string, object?>("component", "engine"));
                Telemetry.CrumbDurationMs.Record(stopwatch.ElapsedMilliseconds, new KeyValuePair<string, object?>("phase", "execution"));
                logger.LogAfterCrumbExecution(ctx);
            }
        });

        Telemetry.CrumbDurationMs.Record(stopwatch.SplitMs(), new KeyValuePair<string, object?>("phase", "preparation"));

        try
        {
            logger.LogBeforeMiddlewares(context);
            // TODO: [P2] do telemetry and span per each middleware
            await middlewaresChain.Process(context);
        }
        catch (Exception exception)
        {
            if (crumbException != exception)
            {
                logger.LogException(exception, context);
            }

            throw;
        }
        finally
        {
            Telemetry.CrumbDurationMs.Record(stopwatch.ElapsedMs(), new KeyValuePair<string, object?>("phase", "total"));
            logger.LogAfterMiddlewares(context);
        }
    }
}
