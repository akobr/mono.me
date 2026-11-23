using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Tells the operator once at start, while the exchange is enabled, how each actor treats the clients of the provider:
// without AllowedClientIds the subject token may come from any client, as long as its aud is one of the actor's
// SubjectAudiences, so it warns unless UntrustedClientRestrictions narrows the clients outside TrustedClientIds. It also
// warns about client lists without effect and about denied clients an actor lists, and counts the denied clients.
// Logging only, without I/O.
internal sealed class TokenExchangeActorNotice : IHostedService
{
    private readonly IOptions<TokenExchangeOptions> _options;
    private readonly ILogger<TokenExchangeActorNotice> _logger;

    public TokenExchangeActorNotice(IOptions<TokenExchangeOptions> options, ILogger<TokenExchangeActorNotice> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        foreach (var actor in options.Actors)
        {
            LogActor(actor);
        }

        var denied = Entries(options.DeniedClientIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Deny wins at the exchange and on live tokens, so such an entry of an actor has no effect.
        foreach (var clientId in denied)
        {
            foreach (var actor in options.Actors.Where(actor => Entries(actor.TrustedClientIds).Concat(Entries(actor.AllowedClientIds))
                .Contains(clientId, StringComparer.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(
                    "The client {ClientId} of Auth:TokenExchange:DeniedClientIds is also in the TrustedClientIds or AllowedClientIds of the actor {ActorId}; it is denied.",
                    clientId,
                    actor.Id);
            }
        }

        // The ids themselves are in the settings.
        if (denied.Count > 0)
        {
            _logger.LogInformation("Auth:TokenExchange:DeniedClientIds blocks {DeniedCount} clients for every actor of the exchange.", denied.Count);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // Trimmed, without blank entries, as the exchange compares them.
    private static IEnumerable<string> Entries(string[] values)
    {
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim());
    }

    private void LogActor(TokenExchangeActorOptions actor)
    {
        var anyClient = actor.AllowedClientIds.Length == 0;
        var untrusted = actor.UntrustedClientRestrictions.Distinct(StringComparer.Ordinal).ToList();
        var trustedCount = Entries(actor.TrustedClientIds).Distinct(StringComparer.Ordinal).Count();

        // Whether a client outside TrustedClientIds gets more than the actor's own restrictions: UntrustedClientRestrictions
        // that only repeat them (NoSecretReveal without AllowSecrets) narrow nothing.
        var narrows = actor.GetRestrictions(null).Count > actor.GetRestrictions().Count;

        if (anyClient && !narrows)
        {
            // Today's exposure: every client the person consents to acts with the actor's whole policy.
            _logger.LogWarning(
                "The actor {ActorId} of Auth:TokenExchange:Actors has no AllowedClientIds: a token any client got for its SubjectAudiences ({SubjectAudiences}) is accepted for the exchange. List the clients to narrow it, or set UntrustedClientRestrictions (for example ReadOnly and NoSecretReveal) for the clients outside TrustedClientIds.",
                actor.Id,
                string.Join(", ", actor.SubjectAudiences));
        }
        else if (anyClient)
        {
            _logger.LogInformation(
                "The actor {ActorId} of Auth:TokenExchange:Actors accepts a token of any client for {SubjectAudiences}; the clients outside its {TrustedCount} TrustedClientIds get {UntrustedClientRestrictions}.",
                actor.Id,
                string.Join(", ", actor.SubjectAudiences),
                trustedCount,
                string.Join(", ", untrusted));
        }

        // AllowSecrets takes NoSecretReveal from the actor's own policy, so only UntrustedClientRestrictions can keep the
        // secrets from a client nobody vetted.
        if (actor.AllowSecrets && anyClient && !untrusted.Contains(ActorRestrictions.NoSecretReveal, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "The actor {ActorId} of Auth:TokenExchange:Actors has AllowSecrets and no AllowedClientIds: every client may read the person's secrets through it; add NoSecretReveal to UntrustedClientRestrictions.",
                actor.Id);
        }

        if (trustedCount > 0 && !narrows)
        {
            _logger.LogWarning(
                "The actor {ActorId} of Auth:TokenExchange:Actors lists {TrustedCount} TrustedClientIds: TrustedClientIds has no effect without UntrustedClientRestrictions that add to the actor's own restrictions.",
                actor.Id,
                trustedCount);
        }

        // With AllowedClientIds, a trusted client that is not allowed never gets a token, so its trust never applies.
        if (!anyClient)
        {
            var allowed = Entries(actor.AllowedClientIds).ToHashSet(StringComparer.Ordinal);

            foreach (var clientId in Entries(actor.TrustedClientIds).Distinct(StringComparer.Ordinal).Where(clientId => !allowed.Contains(clientId)))
            {
                _logger.LogWarning(
                    "The client {ClientId} of the TrustedClientIds of the actor {ActorId} is not in its AllowedClientIds: the exchange refuses it, so its trust never applies.",
                    clientId,
                    actor.Id);
            }
        }
    }
}
