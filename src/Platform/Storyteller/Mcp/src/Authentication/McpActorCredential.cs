using System.IO.Abstractions;

using _42.Platform.Storyteller.Mcp.Hosting;

using Azure;
using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// The proof of Mcp:Actor in the token exchange form, by the one credential its settings name:
// - WorkloadIdentity: a platform identity token as actor_token, which the API's WorkloadIdentity actor checks. A managed
//   identity token for {Audience}/.default from Azure.Identity (which caches it), or the token file, read for every
//   exchange because the platform rotates it.
// - ClientAssertionKey: an RFC 7523 client assertion for the token endpoint, or for its Audience (private_key_jwt).
// - ClientSecret: client_secret (client_secret_post), which the options validator allows in Development only.
public sealed class McpActorCredential
{
    public const string ActorTokenType = "urn:ietf:params:oauth:token-type:jwt";
    public const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly McpActorOptions _actor;
    private readonly McpActorKey _key;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lazy<TokenCredential>? _managedIdentity;

    public McpActorCredential(
        IOptions<McpHostOptions> options,
        McpActorKey key,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<McpActorCredential> logger)
        : this(options, key, fileSystem, timeProvider, logger, managedIdentity: null)
    {
    }

    // Tests give the managed identity; otherwise it is Azure.Identity's, created on first use.
    internal McpActorCredential(
        IOptions<McpHostOptions> options,
        McpActorKey key,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<McpActorCredential> logger,
        TokenCredential? managedIdentity)
    {
        _actor = options.Value.Actor;
        _key = key;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;

        if (_actor is { HasWorkloadIdentity: true, WorkloadIdentity: { Kind: McpWorkloadIdentityKind.ManagedIdentity } identity })
        {
            _managedIdentity = new Lazy<TokenCredential>(() => managedIdentity ?? CreateManagedIdentity(identity));
        }
    }

    public string ActorId => _actor.Id;

    // The form fields of the proof for the exchange at tokenEndpoint. StorytellerActorException when the credential is
    // not available.
    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetProofAsync(string tokenEndpoint, CancellationToken cancellationToken)
    {
        if (_actor.HasWorkloadIdentity)
        {
            return
            [
                new("actor_token", await GetWorkloadTokenAsync(_actor.WorkloadIdentity!, cancellationToken)),
                new("actor_token_type", ActorTokenType),
            ];
        }

        if (_actor.HasClientAssertionKey)
        {
            // The token endpoint, unless the API knows itself by another URL than the server reaches it by.
            var audience = string.IsNullOrWhiteSpace(_actor.ClientAssertionKey!.Audience) ? tokenEndpoint : _actor.ClientAssertionKey.Audience;

            return
            [
                new("client_assertion", _key.CreateClientAssertion(_actor.Id, audience, _timeProvider.GetUtcNow())),
                new("client_assertion_type", ClientAssertionType),
            ];
        }

        if (_actor.HasClientSecret)
        {
            return [new("client_secret", _actor.ClientSecret!)];
        }

        throw new InvalidOperationException("Mcp:Actor has no credential.");
    }

    private static TokenCredential CreateManagedIdentity(McpWorkloadIdentityOptions identity)
    {
        var id = string.IsNullOrWhiteSpace(identity.ClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(identity.ClientId.Trim());

        return new ManagedIdentityCredential(id);
    }

    private async Task<string> GetWorkloadTokenAsync(McpWorkloadIdentityOptions identity, CancellationToken cancellationToken)
    {
        string? token;

        try
        {
            token = identity.Kind == McpWorkloadIdentityKind.ManagedIdentity
                ? (await _managedIdentity!.Value.GetTokenAsync(new TokenRequestContext([$"{identity.Audience!.Trim()}/.default"]), cancellationToken)).Token
                : await _fileSystem.File.ReadAllTextAsync(identity.TokenFilePath!, cancellationToken);
        }
        catch (Exception exception) when (exception is AuthenticationFailedException or RequestFailedException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The workload identity token of Mcp:Actor is not available ({ExceptionType}).", exception.GetType().Name);
            throw new StorytellerActorException(StorytellerActorException.CredentialUnavailableMessage, "actor_token_unavailable");
        }

        token = token?.Trim();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("The workload identity token of Mcp:Actor is empty.");
            throw new StorytellerActorException(StorytellerActorException.CredentialUnavailableMessage, "actor_token_unavailable");
        }

        return token;
    }
}
