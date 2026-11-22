using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Security;

// The logging scope of a request a person makes through an actor: DelegatedActor and DelegatedClient (NoClient when the
// token names none), from the items the bearer middleware stored. Every log written inside it carries both, and
// Application Insights shows them as custom dimensions. Nothing for any other request.
internal static class DelegationLogScope
{
    public const string NoClient = "(none)";

    public static IDisposable? Begin(ILogger logger, FunctionContext context)
    {
        if (!context.Items.TryGetValue(FunctionContextItemKeys.DelegatedActor, out var actor)
            || actor is not string actorId
            || string.IsNullOrEmpty(actorId))
        {
            return null;
        }

        return logger.BeginScope(new Dictionary<string, object>
        {
            [FunctionContextItemKeys.DelegatedActor] = actorId,
            [FunctionContextItemKeys.DelegatedClient] = ClientOf(context),
        });
    }

    // The client of the delegation, or NoClient.
    public static string ClientOf(FunctionContext context)
    {
        return context.Items.TryGetValue(FunctionContextItemKeys.DelegatedClient, out var client)
            && client is string clientId
            && !string.IsNullOrEmpty(clientId)
                ? clientId
                : NoClient;
    }
}
