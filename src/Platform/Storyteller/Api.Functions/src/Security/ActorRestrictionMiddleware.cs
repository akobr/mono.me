using System.Collections.Frozen;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Security;

// The restriction ReadOnly of a person's token through an actor (ActorRestrictions.ReadOnly): every HTTP request other
// than GET, HEAD and OPTIONS is refused with 403 ActorReadOnly, except the functions of ReadOnlyOperations. Registered
// right after BearerAuthenticationMiddleware, which stores the delegation, and inside ExceptionHandlingMiddleware, which
// answers the refusal. It comes before the role checks and the step-up: a method rule refuses uniformly and reveals
// nothing about roles, no new sign-in or role lifts it, and it covers the writes that need no role, the role checks of
// the services and DEV_AUTH builds. Machines, direct users, anonymous endpoints and timers pass unchanged. Every HTTP
// trigger declares its methods and the host names the function, so neither can be spoofed.
public class ActorRestrictionMiddleware : IFunctionsWorkerMiddleware
{
    public const string ReadOnlyMessage = "The token acts for the person through a client app that may only read; the change is refused.";

    private static readonly FrozenSet<string> ReadMethods = FrozenSet.ToFrozenSet(["GET", "HEAD", "OPTIONS"], StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ActorRestrictionMiddleware> _logger;

    public ActorRestrictionMiddleware(ILogger<ActorRestrictionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (IsReadOnly(context) && await context.GetHttpRequestDataAsync() is { } request)
        {
            var functionName = context.FunctionDefinition.Name;

            if (!MayPass(functionName, request.Method))
            {
                // A refused write leaves no audit record, so this line is its trace.
                _logger.LogWarning(
                    "A request {FunctionName} {Method} of the account {AccountId} via {ActorId} (client {ClientId}) is refused: the client app may only read ({Restriction})",
                    functionName,
                    request.Method,
                    request.GetClaim("sub"),
                    request.TryGetDelegatedActor(out var actorId) ? actorId : null,
                    DelegationLogScope.ClientOf(context),
                    ActorRestrictions.ReadOnly);
                throw ReadOnlyRefused();
            }
        }

        await next(context);
    }

    public static AccessDeniedException ReadOnlyRefused()
    {
        return new AccessDeniedException(ReadOnlyMessage, ErrorCodes.ActorReadOnly);
    }

    // True for a method that reads, and for a function that only reads (ReadOnlyOperations).
    internal static bool MayPass(string functionName, string method)
    {
        return ReadMethods.Contains(method) || ReadOnlyOperations.All.Contains(functionName);
    }

    // Only the delegation the bearer middleware stored (FunctionContextItemKeys.DelegatedRestrictions) names it.
    private static bool IsReadOnly(FunctionContext context)
    {
        return context.Items.TryGetValue(FunctionContextItemKeys.DelegatedRestrictions, out var value)
            && value is IReadOnlyCollection<string> restrictions
            && restrictions.Contains(ActorRestrictions.ReadOnly, StringComparer.Ordinal);
    }
}
