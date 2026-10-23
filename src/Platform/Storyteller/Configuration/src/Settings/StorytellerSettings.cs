using System.IO.Abstractions;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// StorytellerConfigurationOptions after the environment filled them in and validation passed; immutable, so a later
// change of the options object changes nothing.
internal sealed class StorytellerSettings
{
    // The API base, /api included, without user info, a query or a fragment.
    public required Uri Endpoint { get; init; }

    // Required by the provider; the default scope of the document service, when set.
    public FullKey? Key { get; init; }

    public StorytellerReadMode Mode { get; init; }

    // Zero disables the refresh.
    public TimeSpan RefreshInterval { get; init; }

    public bool Optional { get; init; }

    // A full path; null without a disk cache.
    public string? CacheDirectory { get; init; }

    // Bound to the endpoint, the project of the key, the primary handler callback, the clock and the resilience options.
    public StorytellerCredential? Credential { get; init; }

    public ISecretReferenceResolver? ReferenceResolver { get; init; }

    public TimeSpan SecretRefreshInterval { get; init; }

    public TimeSpan ReadTimeout { get; init; }

    // A copy of the caller's options.
    public required StorytellerResilienceOptions Resilience { get; init; }

    public SourcePlacement Placement { get; init; }

    public IStorytellerCacheProtector? CacheProtector { get; init; }

    // null when the options set none: AddStorytellerConfiguration() hands over the container's factory later.
    public ILoggerFactory? LoggerFactory { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public Action<SocketsHttpHandler>? ConfigurePrimaryHandler { get; init; }

    // A test seam: replaces the SocketsHttpHandler of the API requests.
    public HttpMessageHandler? PrimaryHandler { get; init; }

    // Uniform in [0, 1), for the jitter of the poll timer.
    public required Func<double> NextRandom { get; init; }
}
