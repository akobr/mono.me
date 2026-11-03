using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace _42.Platform.Storyteller.Sdk.Configuring.KeyVault;

// Resolves the platform's secret references in Azure Key Vault, locally, with the application's own identity: the
// kv://{vault}/{name}[/{version}] shorthand, a vault secret URI and App Configuration's {"uri": …}. It declines every
// other value, and every host or vault the options do not allow, so the next resolver of a FirstOf can take it.
// Failures name the vault and the secret, never a value.
public sealed class KeyVaultReferenceResolver : ISecretReferenceResolver
{
    private const string AuthenticationFailedExceptionType = "Azure.Identity.AuthenticationFailedException";
    private const string MsalServiceExceptionType = "Microsoft.Identity.Client.MsalServiceException";

    private readonly string _defaultVaultSuffix;
    private readonly string[] _allowedHostSuffixes;
    private readonly HashSet<string> _allowedVaults;
    private readonly Func<Uri, SecretClient> _createClient;

    // One client per vault origin; Lazy, so a race creates one.
    private readonly ConcurrentDictionary<string, Lazy<SecretClient>> _clients = new(StringComparer.Ordinal);

    public KeyVaultReferenceResolver(TokenCredential credential, KeyVaultReferenceResolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(credential);

        options ??= new KeyVaultReferenceResolverOptions();
        _defaultVaultSuffix = NormalizeSuffix(options.DefaultVaultSuffix)
            ?? throw InvalidSuffix(nameof(options.DefaultVaultSuffix), options.DefaultVaultSuffix, nameof(options));
        _allowedHostSuffixes =
        [
            .. options.AllowedHostSuffixes
                .Select(suffix => NormalizeSuffix(suffix) ?? throw InvalidSuffix(nameof(options.AllowedHostSuffixes), suffix, nameof(options)))
                .Append(_defaultVaultSuffix)
                .Distinct(StringComparer.Ordinal),
        ];
        _allowedVaults = new HashSet<string>(
            options.AllowedVaults.Select(vault => string.IsNullOrWhiteSpace(vault)
                ? throw new ArgumentException($"{nameof(options.AllowedVaults)} holds an empty vault name.", nameof(options))
                : vault.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var clientOptions = options.SecretClientOptions;
        _createClient = options.SecretClientFactory
            ?? (vaultUri => clientOptions is null ? new SecretClient(vaultUri, credential) : new SecretClient(vaultUri, credential, clientOptions));
    }

    public bool CanResolve(string value)
    {
        return TryGetTarget(value, out _);
    }

    public async ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken)
    {
        if (!TryGetTarget(value, out var target))
        {
            throw new SecretResolutionException("The Key Vault resolver does not accept the value; call CanResolve first.", false);
        }

        KeyVaultSecret secret;

        try
        {
            var client = _clients.GetOrAdd(target.Origin, static (_, state) => new Lazy<SecretClient>(() => state.Create(state.Uri)), (Create: _createClient, Uri: target.VaultUri)).Value;
            secret = (await client.GetSecretAsync(target.Name, target.Version, cancellationToken).ConfigureAwait(false)).Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled: what failed then is its cancellation, also the AggregateException of the client's
            // retries that gave up for it.
            throw new OperationCanceledException($"Reading the secret {target.Name} of the vault {target.Host} was cancelled.", exception, cancellationToken);
        }
        catch (Exception exception)
        {
            throw Failed(target, exception);
        }

        return secret.Value
            ?? throw new SecretResolutionException($"The secret {target.Name} of the vault {target.Host} has no value.", false, target.VaultName, target.Name);
    }

    // 408, 429, 5xx and no answer at all (status 0, a network error after the retries of the client) are worth another
    // try; 401, 403 (also a disabled secret), 404 and the other answers come again.
    internal static bool IsTransientStatus(int status)
    {
        return status is 0 or 408 or 429 or >= 500;
    }

    // Network errors and timeouts are worth another try. Once the retries of a SecretClient give up, Azure.Core puts the
    // failure of every attempt in an AggregateException, which is transient when all of them are. A caller's
    // cancellation never gets here, so a cancellation is a timeout of the client's own.
    internal static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            RequestFailedException failed => IsTransientStatus(failed.Status),
            HttpRequestException or IOException or TimeoutException or OperationCanceledException => true,
            AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsTransient),

            // A credential whose token request failed. Azure.Identity, which this package does not reference, wraps the
            // failure in AuthenticationFailedException. The exact type: its subclass CredentialUnavailableException is a
            // credential that cannot work here, also a managed identity endpoint that did not answer, and stays definitive.
            { InnerException: { } inner } when exception.GetType().FullName == AuthenticationFailedExceptionType => IsTransient(inner),

            // Entra's answer to the token request, through MSAL: its status, or without an answer, its cause.
            _ when IsOfType(exception, MsalServiceExceptionType) => GetStatusCode(exception) switch
            {
                0 => exception.InnerException is { } cause && IsTransient(cause),
                var status => IsTransientStatus(status),
            },
            _ => false,
        };
    }

    // The status and the error code only: Key Vault's error text is in the inner exception, and never holds a value. A
    // failure after retries is described by its last attempt.
    private static SecretResolutionException Failed(Target target, Exception exception)
    {
        IReadOnlyList<Exception> attempts = exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [exception];
        var last = attempts.Count > 0 ? attempts[^1] : exception;
        var retried = attempts.Count > 1 ? $" after {attempts.Count} attempts" : string.Empty;
        var isTransient = IsTransient(exception);

        if (last is not RequestFailedException failed)
        {
            // A credential that cannot get a token, among others. Its message stays in the inner exception.
            var types = string.Join(", ", attempts.Select(attempt => attempt.GetType().Name).Distinct(StringComparer.Ordinal));
            var next = isTransient ? "; it is tried again later" : string.Empty;

            return new SecretResolutionException(
                $"Reading the secret {target.Name} of the vault {target.Host} failed{retried} with {types}{next}.",
                isTransient,
                target.VaultName,
                target.Name,
                exception);
        }

        var status = failed.Status;
        var code = string.IsNullOrWhiteSpace(failed.ErrorCode) ? string.Empty : $" {failed.ErrorCode}";
        var secret = target.Version is null ? $"the secret {target.Name}" : $"version {target.Version} of the secret {target.Name}";
        var reason = status switch
        {
            0 => $"Key Vault {target.Host} did not answer the read of {secret}",
            401 => $"Key Vault {target.Host} refused the credential of the application for {secret}",
            403 => $"The application may not read {secret} of the vault {target.Host}, or the secret is disabled",
            404 => $"The vault {target.Host} does not hold {secret}",
            408 or 429 => $"Key Vault {target.Host} is busy; it did not return {secret}",
            >= 500 => $"Key Vault {target.Host} failed to return {secret}",
            _ => $"Key Vault {target.Host} did not return {secret}",
        };

        return new SecretResolutionException($"{reason}{retried} ({status}{code}).", isTransient, target.VaultName, target.Name, exception);
    }

    // Lower-case ASCII (an IDN suffix as punycode), without a leading dot; null when it is not a host name.
    private static string? NormalizeSuffix(string? suffix)
    {
        var text = suffix?.Trim().TrimStart('.');

        if (string.IsNullOrEmpty(text) || text.EndsWith('.') || Uri.CheckHostName(text) != UriHostNameType.Dns)
        {
            return null;
        }

        try
        {
            return new IdnMapping().GetAscii(text).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static ArgumentException InvalidSuffix(string optionName, string? suffix, string parameterName)
    {
        return new ArgumentException(
            $"{optionName} holds '{suffix}', which is not a host name suffix such as {KeyVaultReferenceResolverOptions.AzureVaultSuffix}.",
            parameterName);
    }

    private static bool HasSuffix(string host, string suffix)
    {
        return host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            || (host.Length > suffix.Length
                && host[host.Length - suffix.Length - 1] == '.'
                && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    // By name, with its subclasses, such as MSAL's MsalThrottledServiceException.
    private static bool IsOfType(Exception exception, string fullName)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == fullName)
            {
                return true;
            }
        }

        return false;
    }

    // MsalServiceException.StatusCode: the HTTP status of Entra's answer, 0 without one.
    private static int GetStatusCode(Exception exception)
    {
        return exception.GetType().GetProperty("StatusCode", BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception) is int status ? status : 0;
    }

    private bool TryGetTarget(string value, out Target target)
    {
        target = default;

        if (!SecretReference.TryParse(value, out var reference))
        {
            return false;
        }

        string host;
        string vaultName;

        if (reference.Form == SecretReferenceForm.Shorthand)
        {
            // Vault names compare without case, as Key Vault does.
            vaultName = reference.VaultName!;
            host = $"{vaultName}.{_defaultVaultSuffix}".ToLowerInvariant();
        }
        else
        {
            // SecretReference admits only hosts with an ASCII form; a value is declined rather than failed all the same.
            try
            {
                host = reference.VaultUri!.IdnHost.ToLowerInvariant();
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!Array.Exists(_allowedHostSuffixes, suffix => HasSuffix(host, suffix)))
            {
                return false;
            }

            var dot = host.IndexOf('.', StringComparison.Ordinal);
            vaultName = dot < 0 ? host : host[..dot];
        }

        if (_allowedVaults.Count > 0 && !_allowedVaults.Contains(vaultName))
        {
            return false;
        }

        target = new Target($"https://{host}", new Uri($"https://{host}/"), vaultName, host, reference.Name, reference.Version);
        return true;
    }

    // Where a reference points: the vault origin and URI, its name for AllowedVaults and messages, and the secret.
    private readonly record struct Target(string Origin, Uri VaultUri, string VaultName, string Host, string Name, string? Version);
}
