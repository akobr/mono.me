using System.Net;

namespace _42.Platform.Storyteller.KeyValues;

// Factories for the domain errors of key/value pairs. The generic bases carry the stable error codes.
public static class KeyValueErrors
{
    public static NotFoundException NotFound(string view, string key)
    {
        return new NotFoundException($"The key '{key}' does not exist in view '{view}'.", ErrorCodes.KeyValueNotFound);
    }

    public static ConflictException Busy(string view, string key, int attempts)
    {
        return new ConflictException(
            $"The key '{key}' in view '{view}' was changed concurrently {attempts} times; retry the request.",
            ErrorCodes.KeyValueBusy);
    }

    public static PreconditionFailedException VersionMismatch(string key, ulong expected, ulong actual)
    {
        return new PreconditionFailedException(
            $"The key '{key}' is at version {actual}, not {expected}. Reload and retry.",
            ErrorCodes.KeyValueVersionMismatch);
    }

    public static ConflictException ProtectionNotAllowed(KeyValueProtection protection)
    {
        return new ConflictException(
            $"The protection level '{protection}' is not allowed in this project.",
            ErrorCodes.ProtectionNotAllowed);
    }

    public static FeatureNotConfiguredException ProtectionNotConfigured(KeyValueProtection protection)
    {
        return new FeatureNotConfiguredException(
            $"The protection level '{protection}' is not configured in this deployment.",
            ErrorCodes.ProtectionNotConfigured);
    }

    public static ServiceUnavailableException KeyEncryptionUnavailable(KeyValueProtection protection, TimeSpan? retryAfter, Exception cause)
    {
        return new ServiceUnavailableException(
            $"The key-encryption key of the '{protection}' level is not available right now.",
            ErrorCodes.KeyEncryptionUnavailable,
            retryAfter,
            cause);
    }

    public static ConflictException ProtectionInUse(KeyValueProtection protection, IReadOnlyDictionary<string, int> countsPerView)
    {
        var detail = string.Join(", ", countsPerView.Select(entry => $"{entry.Key}: {entry.Value}"));
        return new ConflictException(
            $"The protection level '{protection}' is still used by keys of the project ({detail}); change them first.",
            ErrorCodes.ProtectionInUse);
    }

    public static FailedDependencyException CustomerKeyUnavailable(string reason, Exception? cause = null)
    {
        return new FailedDependencyException(
            $"The customer's key is not available: {reason}",
            ErrorCodes.CustomerKeyUnavailable,
            cause);
    }

    public static AccessDeniedException MtlsRequired()
    {
        return new AccessDeniedException(
            "Secret values are delivered to machines only over mTLS in this project.",
            ErrorCodes.MtlsRequired);
    }

    public static InvalidInputException ProtectionRequired(string key)
    {
        return new InvalidInputException(
            $"The protection level is required to create the key '{key}'.",
            ErrorCodes.ProtectionRequired);
    }

    public static AccessDeniedException SecretRightsRequired(string key)
    {
        return new AccessDeniedException(
            $"The key '{key}' is a secret; the caller has no rights to secrets.");
    }
}

// The ciphertext of a stored version cannot be decrypted (a wrong tag, a missing part, or data moved between keys).
public sealed class KeyValueIntegrityException(string message, Exception? innerException = null)
    : Exception(message, innerException);

// An unexpected failure of the key/value storage (not caused by the input of the request).
public sealed class KeyValueStorageException(string message, HttpStatusCode statusCode)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

// An import document with invalid items; nothing was written. Items carry the error code per key.
public sealed class KeyValueImportInvalidException(string message, IReadOnlyList<KeyValueImportItemResult> items)
    : StorytellerException(message, ErrorCodes.ImportInvalid)
{
    public IReadOnlyList<KeyValueImportItemResult> Items { get; } = items;
}
