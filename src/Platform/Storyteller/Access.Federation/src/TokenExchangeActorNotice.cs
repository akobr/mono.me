using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Warns once at start about each actor of an enabled exchange without AllowedClientIds: the subject token may then come
// from any client of the provider, as long as its aud is one of the actor's SubjectAudiences. Logging only, without I/O.
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

        foreach (var actor in options.Actors.Where(actor => actor.AllowedClientIds.Length == 0))
        {
            _logger.LogWarning(
                "The actor {ActorId} of Auth:TokenExchange:Actors has no AllowedClientIds: a token any client got for its SubjectAudiences ({SubjectAudiences}) is accepted for the exchange. List the clients to narrow it.",
                actor.Id,
                string.Join(", ", actor.SubjectAudiences));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
