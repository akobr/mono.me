using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Configuring.Caching;

// Who reads a document, for the cache identity: the kind of the credential, plus what tells two principals of one kind
// apart, all hashed. The SDK keeps its credentials' fields to itself, so the public surface is read:
// - ToString(), which every SDK kind prints without secrets and with the client id, the subject token path (or the
//   GitHub Actions source) or the certificate thumbprint; CredentialFingerprintTests pins the federated texts;
// - the thumbprint of ClientCertificate;
// - the machine segment of an API key, 2s.{base64url(organization:project:machine)}.{secret}.
// A credential of the application is told apart by its type, ToString() and certificate, plus the machine of an API
// key it has at hand: one it answers synchronously. One that fetches its key (from a vault, say) must name its
// principal in ToString(), without secrets, or its principals share entries.
internal static class CredentialFingerprint
{
    public const string Anonymous = "anonymous";

    private const string ApiKeyScheme = "ApiKey";
    private const int HashLength = 32;

    public static async ValueTask<string> ComputeAsync(StorytellerCredential? credential, CancellationToken cancellationToken)
    {
        if (credential is null)
        {
            return Anonymous;
        }

        var details = new StringBuilder();
        details.Append(credential.ToString()).Append('\n');

        if (credential.ClientCertificate is { } certificate)
        {
            details.Append("certificate:").Append(Hash(certificate.Thumbprint)).Append('\n');
        }

        // An API key of the SDK is static and costs nothing to ask for. A renewable credential may request a token,
        // which can be the very request that is failing or hanging when the cache is needed; the fingerprint would
        // then differ from the one the entry was written under. So only an answer at hand counts for the application.
        string? apiKey = null;

        if (credential.GetType().Assembly != typeof(StorytellerCredential).Assembly)
        {
            apiKey = ReadApiKeyAtHand(credential, cancellationToken);
        }
        else if (!credential.CanRenew)
        {
            apiKey = await ReadApiKeyAsync(credential, cancellationToken).ConfigureAwait(false);
        }

        if (apiKey is not null)
        {
            details.Append("api-key:").Append(ReadMachine(apiKey) ?? Hash(apiKey)).Append('\n');
        }

        return $"{credential.GetType().Name}:{Hash(details.ToString())}";
    }

    private static async ValueTask<string?> ReadApiKeyAsync(StorytellerCredential credential, CancellationToken cancellationToken)
    {
        AuthenticationHeaderValue? authorization;

        try
        {
            authorization = await credential.GetAuthorizationAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A token file that is missing now; the read reports it.
            return null;
        }

        return ToApiKey(authorization);
    }

    // Never waits: an answer that is not complete on return depends on something that may be down, and is left to run.
    private static string? ReadApiKeyAtHand(StorytellerCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            var pending = credential.GetAuthorizationAsync(null, cancellationToken);

            if (!pending.IsCompleted)
            {
                _ = pending.AsTask().ContinueWith(
                    static completed => _ = completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return null;
            }

            return ToApiKey(pending.GetAwaiter().GetResult());
        }
        catch (Exception)
        {
            // A failure is no answer, whatever its reason; the read reports it.
            return null;
        }
    }

    private static string? ToApiKey(AuthenticationHeaderValue? authorization)
    {
        return authorization is { Scheme: var scheme, Parameter: { Length: > 0 } parameter }
            && string.Equals(scheme, ApiKeyScheme, StringComparison.OrdinalIgnoreCase)
                ? parameter
                : null;
    }

    // The middle segment names the organization, project and machine, and is no secret.
    private static string? ReadMachine(string apiKey)
    {
        var segments = apiKey.Split('.', 3);
        return segments is ["2s", { Length: > 0 } machine, { Length: > 0 }] ? machine : null;
    }

    private static string Hash(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength];
    }
}
