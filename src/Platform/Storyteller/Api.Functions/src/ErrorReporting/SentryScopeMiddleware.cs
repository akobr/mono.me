using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

using Sentry;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// The first middleware when Sentry is on: every invocation gets a scope of its own, so its events carry the route
// template, the function and the invocation id, and only its own breadcrumbs. The scope starts empty: PushScope clones
// the root scope, which gathers the breadcrumbs logged outside invocations (hosted services, refresh callbacks), and
// its trace id is new, so invocations are not tied together. Nothing of the request (URL, route values, headers, body)
// is put on it.
internal sealed class SentryScopeMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IHub _hub;

    public SentryScopeMiddleware(IHub hub)
    {
        _hub = hub;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var name = context.FunctionDefinition.Name;
        var request = await context.GetHttpRequestDataAsync();
        var transaction = FunctionTransactionNames.Get(name, request?.Method);

        using (_hub.PushScope())
        {
            _hub.ContinueTrace((string?)null, null);
            _hub.ConfigureScope(scope =>
            {
                scope.ClearBreadcrumbs();
                SentryScrubber.SetFunction(scope, name, transaction, context.InvocationId);
            });

            await next(context);
        }
    }
}
