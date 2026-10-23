using System.IO.Abstractions;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Configuring;

// The settings of a Storyteller source. The configure callback runs first; every nullable property it left null is
// then read from its STORYTELLER_* variable. Validated once, when the source is added.
public sealed class StorytellerConfigurationOptions
{
    // The API base, /api included: STORYTELLER_ENDPOINT, or STORYTELLER_API_URL as the sform fallback. https, or http
    // to localhost or a loopback address.
    public string? Endpoint { get; set; }

    // The full key of the configuration, @{organization}.{project}.{view}.{annotation key}: STORYTELLER_KEY.
    public string? Key { get; set; }

    // STORYTELLER_MODE, resolved or effective; Resolved when unset.
    public StorytellerReadMode? Mode { get; set; }

    // STORYTELLER_REFRESH_INTERVAL; 30 s when unset. Zero disables the refresh; otherwise at least one second.
    public TimeSpan? RefreshInterval { get; set; }

    // STORYTELLER_OPTIONAL: start empty when the platform is unreachable and nothing is cached. A wrong key, a refused
    // credential or withheld secrets still fail.
    public bool? Optional { get; set; }

    // STORYTELLER_CACHE_DIR; no disk cache when unset.
    public string? CacheDirectory { get; set; }

    // StorytellerCredential.FromEnvironment() when unset; anonymous requests, which the API answers 401, when that
    // finds none. Bound to the validated endpoint and key.
    public StorytellerCredential? Credential { get; set; }

    // Resolves the secret references of resolved documents; no substitution when unset. Several resolvers combine
    // with SecretReferenceResolver.FirstOf.
    public ISecretReferenceResolver? ReferenceResolver { get; set; }

    // How long a resolved secret is reused before it is resolved again, also on a 304.
    public TimeSpan SecretRefreshInterval { get; set; } = TimeSpan.FromHours(1);

    // Bounds one whole run (startup, refresh or switch), retries and token requests included.
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public StorytellerResilienceOptions Resilience { get; set; } = new();

    public SourcePlacement Placement { get; set; } = SourcePlacement.BeforeEnvironmentVariables;

    // Protects every cache entry; without one, responses marked no-store are not cached.
    public IStorytellerCacheProtector? CacheProtector { get; set; }

    // Silent until AddStorytellerConfiguration() hands over the container's factory, when unset.
    public ILoggerFactory? LoggerFactory { get; set; }

    // TimeProvider.System when unset.
    public TimeProvider? TimeProvider { get; set; }

    // A FileSystem over System.IO when unset.
    public IFileSystem? FileSystem { get; set; }

    // Runs on the primary handler of the API and of token requests, for proxies and TLS settings. The client
    // certificate and AllowAutoRedirect = false are set again after it.
    public Action<SocketsHttpHandler>? ConfigurePrimaryHandler { get; set; }

    // The seams of tests: the environment, the primary handler of the API requests, and the random source of the jitter
    // (uniform in [0, 1)).
    internal Func<string, string?>? GetEnvironmentVariable { get; set; }

    internal HttpMessageHandler? PrimaryHandler { get; set; }

    internal Func<double>? NextRandom { get; set; }
}
