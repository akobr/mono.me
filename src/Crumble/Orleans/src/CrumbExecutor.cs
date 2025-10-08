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
        var contextKey = crumbGrain.GetPrimaryKeyString();
        // TODO: [P2] work with flags and properties in a better way if possible
        var context = OrleansCrumbExecutionContext.FromRequestContext();
        context.ContextKey = contextKey;
        return context;
    }

    public TCrumbInstance CreateCrumbInstance<TCrumbInstance>()
    {
        return ActivatorUtilities.GetServiceOrCreateInstance<TCrumbInstance>(_services);
    }

    public async Task ExecuteCrumbWithMiddlewares(ICrumbInnerExecutionContext context, Func<Task> crumbAction)
    {
        var middlewaresProvider = _services.GetRequiredService<IMiddlewaresProvider>();
        using var logger = _services.GetRequiredService<ICrumbLogger>();
        Exception? crumbException = null;

        var middlewaresChain = middlewaresProvider.GetMiddlewareFullChain(async ctx =>
        {
            try
            {
                logger.LogBeforeCrumbExecution(ctx);
                await crumbAction();
            }
            catch (Exception exception)
            {
                logger.LogException(exception, ctx);
                crumbException = exception;
                throw;
            }
            finally
            {
                logger.LogAfterCrumbExecution(ctx);
            }
        });

        try
        {
            logger.LogBeforeMiddlewares(context);
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
            logger.LogAfterMiddlewares(context);
        }
    }
}
