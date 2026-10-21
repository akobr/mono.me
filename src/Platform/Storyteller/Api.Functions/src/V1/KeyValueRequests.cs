using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.KeyValues;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.V1;

// The authorization rules shared by the key/value endpoints (spec section 24).
internal static class KeyValueAuthorization
{
    // Administrators only, and never a machine: the policy, purges, permanent deletes, the audit trail and usage.
    public static async Task CheckAdministratorAsync(HttpRequestData request, IAccessService access, string organization, string project)
    {
        request.EnsureUser("Only a user with the Administrator role may call this operation; machines cannot.");
        await request.CheckUserAccessToProjectAsync(access, organization, project, AccountRole.Administrator);
    }

    // An explicit request for secret values (a reveal, an export with secrets) by a caller without the read rights.
    public static void EnsureSecretReadRights(SecretRights rights)
    {
        if (rights.CanReadSecrets)
        {
            return;
        }

        throw Refused(rights.IsReadWithheldByMtls);
    }

    // A write that is all about secrets (protect) by a caller without the write rights; the service checks the same bit.
    public static void EnsureSecretWriteRights(SecretRights rights)
    {
        if (rights.CanWriteSecrets)
        {
            return;
        }

        throw Refused(rights.IsWriteWithheldByMtls);
    }

    // A read the service may refuse for a secret it would reveal (the diff of a secret's text).
    public static Task<T> UnderMtlsReadRuleAsync<T>(SecretRights rights, Func<Task<T>> action)
    {
        return UnderMtlsRuleAsync(rights.IsReadWithheldByMtls, action);
    }

    // A write the service may refuse for a secret key or item (set, delete, import).
    public static Task<T> UnderMtlsWriteRuleAsync<T>(SecretRights rights, Func<Task<T>> action)
    {
        return UnderMtlsRuleAsync(rights.IsWriteWithheldByMtls, action);
    }

    // The service refuses a secret with AccessDenied; when the only reason is the missing mTLS, the client learns that.
    // Only the right of the direction counts: a machine without it would not get it over mTLS either.
    private static async Task<T> UnderMtlsRuleAsync<T>(bool isWithheldByMtls, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (AccessDeniedException exception) when (isWithheldByMtls && exception.ErrorCode == ErrorCodes.AccessDenied)
        {
            throw KeyValueErrors.MtlsRequired();
        }
    }

    // When the right was withheld only for the missing mTLS, the client learns that.
    private static AccessDeniedException Refused(bool isWithheldByMtls)
    {
        return isWithheldByMtls
            ? KeyValueErrors.MtlsRequired()
            : new AccessDeniedException("The caller has no rights to the secrets of the project.");
    }
}

// Query parameters of the key/value endpoints; a value that does not parse is 400 InvalidQuery.
internal static class KeyValueQuery
{
    public static bool GetBool(HttpRequestData request, string name)
    {
        var raw = request.Query[name];

        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        return bool.TryParse(raw, out var value)
            ? value
            : throw Invalid(name, "true or false");
    }

    public static ulong? GetVersion(HttpRequestData request, string name)
    {
        var raw = request.Query[name];

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return ulong.TryParse(raw, out var value)
            ? value
            : throw Invalid(name, "a version number");
    }

    public static TEnum? GetEnum<TEnum>(HttpRequestData request, string name)
        where TEnum : struct, Enum
    {
        var raw = request.Query[name];
        return string.IsNullOrEmpty(raw) ? null : ParseEnum<TEnum>(name, raw);
    }

    // Repeatable (?protection=Public&protection=Standard) or comma-separated.
    public static IReadOnlyCollection<KeyValueProtection>? GetProtections(HttpRequestData request)
    {
        var values = request.Query.GetValues(Definitions.Parameters.Protection);

        if (values is null || values.Length == 0)
        {
            return null;
        }

        var protections = values
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => ParseEnum<KeyValueProtection>(Definitions.Parameters.Protection, value))
            .Distinct()
            .ToList();

        return protections.Count == 0 ? null : protections;
    }

    public static DateTimeOffset? GetTime(HttpRequestData request, string name)
    {
        var raw = request.Query[name];

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : throw Invalid(name, "an ISO 8601 date or time");
    }

    public static DateOnly? GetDate(HttpRequestData request, string name)
    {
        var raw = request.Query[name];

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var date))
        {
            return date;
        }

        return DateTimeOffset.TryParse(raw, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
            ? DateOnly.FromDateTime(time.UtcDateTime)
            : throw Invalid(name, "a date (yyyy-MM-dd)");
    }

    private static TEnum ParseEnum<TEnum>(string name, string raw)
        where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw Invalid(name, string.Join(", ", Enum.GetNames<TEnum>()));
    }

    private static InvalidInputException Invalid(string name, string expected)
    {
        return new InvalidInputException($"The query parameter '{name}' must be {expected}.", ErrorCodes.InvalidQuery);
    }
}
