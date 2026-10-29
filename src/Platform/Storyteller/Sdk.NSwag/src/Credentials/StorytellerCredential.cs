using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// How a machine authenticates to the Storyteller API: an Authorization value, a client certificate, or both.
// Immutable; an application or a test can derive its own kind. Disposing a credential closes the connections of its token
// requests; a client certificate stays the caller's.
public abstract class StorytellerCredential : IDisposable
{
    // The certificate for mutual TLS, if any.
    public virtual X509Certificate2? ClientCertificate => null;

    // Whether a value the API refused can be replaced by a new one.
    public virtual bool CanRenew => false;

    public static StorytellerCredential FromApiKey(string apiKey)
    {
        return new ApiKeyCredential(apiKey, null);
    }

    public static StorytellerCredential FromCertificate(X509Certificate2 certificate)
    {
        return new CertificateCredential(RequirePrivateKey(certificate));
    }

    public static StorytellerCredential FromClientCredentials(ClientCredentialsOptions options)
    {
        return ClientCredentialsCredential.Create(options, Environment.GetEnvironmentVariable);
    }

    // The content of the file, read again before every request, is a bearer token.
    public static StorytellerCredential FromTokenFile(string path, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TokenFileCredential(path, fileSystem ?? new FileSystem(), null);
    }

    public static StorytellerCredential FromFederatedTokenFile(FederatedTokenOptions options)
    {
        return FederatedTokenCredential.Create(options, null, Environment.GetEnvironmentVariable);
    }

    // The credential the STORYTELLER_* variables describe, still unbound; null when they describe none. See
    // StorytellerCredentialVariables for the rules.
    public static StorytellerCredential? FromEnvironment(
        Func<string, string?>? getVariable = null,
        IFileSystem? fileSystem = null,
        TimeProvider? timeProvider = null)
    {
        return StorytellerCredentialVariables.Read(getVariable)?.CreateCredential(fileSystem, timeProvider);
    }

    // rejected is the value the API refused, null for a normal call. A cached value is dropped only when it equals
    // rejected, so a burst of 401s renews once.
    public abstract ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken);

    // An API key with the certificate of its machine (or a shared one); the only combination with a certificate.
    public StorytellerCredential WithClientCertificate(X509Certificate2 certificate)
    {
        return this is ApiKeyCredential apiKey
            ? apiKey.WithCertificate(RequirePrivateKey(certificate))
            : throw new InvalidOperationException("A client certificate combines only with an API key; use FromCertificate for a certificate alone.");
    }

    // A copy that knows the context of its requests; this for kinds that need none. Bind once and reuse the copy: a
    // client-credentials or federated copy holds its token and the connection pool of its token requests.
    public virtual StorytellerCredential Bind(StorytellerCredentialBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return this;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    // The seam of tests: token and discovery requests go to this handler instead of the network.
    internal virtual StorytellerCredential WithPrimaryHandler(HttpMessageHandler primaryHandler)
    {
        return this;
    }

    private static X509Certificate2 RequirePrivateKey(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return certificate.HasPrivateKey
            ? certificate
            : throw new ArgumentException("The client certificate has no private key; mutual TLS needs one.", nameof(certificate));
    }
}
