using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

using Sentry;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// The first middleware when Sentry is on: every invocation gets a scope of its own, so its events carry the route
// template, the function and the invocation id, and only its own breadcrumbs. The scope starts empty and with a trace id
// of its own, so invocations are not tied together. Breadcrumbs are kept only while an invocation runs (IsInInvocation,
// checked by BeforeBreadcrumb): the root scope, which every scope is cloned from, never gathers the logs of hosted
// services, refresh callbacks or the worker. Nothing of the request (URL, route values, headers, body) is put on it.
internal sealed class SentryScopeMiddleware : IFunctionsWorkerMiddleware
{
    // Flows into the function's awaits; a caller (the worker, the proxying middleware) never sees it set.
    private static readonly AsyncLocal<bool> InInvocation = new();

    private readonly IHub _hub;

    public SentryScopeMiddleware(IHub hub)
    {
        _hub = hub;
    }

    public static bool IsInInvocation => InInvocation.Value;

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

            InInvocation.Value = true;

            try
            {
                await next(context);
            }
            finally
            {
                InInvocation.Value = false;
            }
        }
    }
}
