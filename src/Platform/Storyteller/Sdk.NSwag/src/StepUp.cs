using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk;

// The step-up of the Storyteller API (Auth:StepUp, RFC 9470): a user whose sign-in is older than the window gets 401 with
// the error code StepUpRequired, Details.MaxAge in seconds, and WWW-Authenticate: Bearer
// error="insufficient_user_authentication", max_age. Signing in again helps; a refresh of the token does not, because it
// keeps the time of the sign-in.
public static class StepUp
{
    public const string ErrorCode = "StepUpRequired";

    public const string ChallengeError = "insufficient_user_authentication";

    private const string WwwAuthenticateHeader = "WWW-Authenticate";

    private static readonly Regex MaxAgeParameter = new(
        "max_age\\s*=\\s*\"?(?<seconds>\\d+)\"?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    // "10 minutes", "1 hour", "90 seconds": the largest unit that divides the window, as the API writes it.
    public static string DescribeWindow(TimeSpan duration)
    {
        var seconds = (long)Math.Floor(duration.TotalSeconds);

        if (seconds > 0 && seconds % 3600 == 0)
        {
            return Plural(seconds / 3600, "hour");
        }

        return seconds > 0 && seconds % 60 == 0
            ? Plural(seconds / 60, "minute")
            : Plural(seconds, "second");
    }

    public static bool IsStepUpRequired(this ApiException exception)
    {
        return exception.IsStepUpRequired(out _);
    }

    // A 401 that names the step-up by its error code (the typed body or the JSON text) or by its challenge. maxAge is the
    // window the API names, from the challenge or the body, or null when neither says.
    public static bool IsStepUpRequired(this ApiException exception, out TimeSpan? maxAge)
    {
        ArgumentNullException.ThrowIfNull(exception);
        maxAge = null;

        if (exception.StatusCode != 401)
        {
            return false;
        }

        var body = ReadBody(exception);
        var challenge = GetChallenge(exception.Headers);
        var byCode = string.Equals(GetErrorCode(exception, body), ErrorCode, StringComparison.Ordinal);
        var byChallenge = challenge is not null && challenge.Contains(ChallengeError, StringComparison.OrdinalIgnoreCase);

        if (!byCode && !byChallenge)
        {
            return false;
        }

        maxAge = GetChallengeMaxAge(challenge) ?? GetBodyMaxAge(body) ?? GetTypedMaxAge(exception);
        return true;
    }

    private static string Plural(long count, string unit)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{count} {unit}{(count == 1 ? string.Empty : "s")}");
    }

    // The typed body of a 401 the document describes, when its text is not kept.
    private static TimeSpan? GetTypedMaxAge(ApiException exception)
    {
        return exception is ApiException<ErrorResponse> { Result.Details: { } details }
            ? GetBodyMaxAge(new JObject { ["Details"] = details as JToken ?? JToken.FromObject(details) })
            : null;
    }

    private static string? GetErrorCode(ApiException exception, JObject? body)
    {
        return exception is ApiException<ErrorResponse> { Result.ErrorCode: { } code }
            ? code
            : body?.Value<string>("ErrorCode");
    }

    private static JObject? ReadBody(ApiException exception)
    {
        if (string.IsNullOrWhiteSpace(exception.Response) || !exception.Response.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JObject.Parse(exception.Response);
        }
        catch (JsonReaderException)
        {
            return null;
        }
    }

    private static string? GetChallenge(IReadOnlyDictionary<string, IEnumerable<string>>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        var values = headers
            .Where(header => string.Equals(header.Key, WwwAuthenticateHeader, StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .ToList();

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static TimeSpan? GetChallengeMaxAge(string? challenge)
    {
        if (challenge is null)
        {
            return null;
        }

        var match = MaxAgeParameter.Match(challenge);
        return match.Success && long.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static TimeSpan? GetBodyMaxAge(JObject? body)
    {
        return body?["Details"] is JObject details
            && details["MaxAge"] is JValue { Type: JTokenType.Integer } value
            && value.Value<long>() is >= 0 and var seconds
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}

// The values of X-2S-Secrets-Withheld (ConditionalResponse.SecretsWithheld): why the caller's right to read secrets was
// withheld from a resolved read.
public static class SecretsWithheldReasons
{
    // A machine that may read secrets called without its client certificate, and the project delivers them only over mTLS.
    public const string MtlsRequired = "mtls-required";

    // A user who may read secrets acts through an actor that may not reveal them (NoSecretReveal).
    public const string ActorRestricted = "actor-restricted";

    // A user who may read secrets signed in longer ago than the API's step-up window: sign in again.
    public const string StepUpRequired = "step-up-required";
}
