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

    // Far beyond any window the API sends (at most a day), far below what TimeSpan overflows at.
    private const long MaxWindowSeconds = 366L * 24 * 60 * 60;

    // An auth-param of a challenge (RFC 9110, 11.2): a name, then a token or a quoted string. Matched from left to right, a
    // quoted value is consumed whole, so a parameter named inside another one's value (error_description) is never read.
    private static readonly Regex AuthParameter = new(
        """(?<name>[A-Za-z0-9!#$%&'*+.^_`|~-]+)\s*=\s*(?:"(?<quoted>(?:[^"\\]|\\.)*)"|(?<token>[^\s,"]+))""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex QuotedPair = new(
        """\\(.)""",
        RegexOptions.CultureInvariant | RegexOptions.Singleline,
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
        var byChallenge = string.Equals(GetChallengeParameter(challenge, "error"), ChallengeError, StringComparison.OrdinalIgnoreCase);

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
            : body?["ErrorCode"] is JValue { Type: JTokenType.String } bodyCode ? (string?)bodyCode : null;
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
        return long.TryParse(GetChallengeParameter(challenge, "max_age"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? ToWindow(seconds)
            : null;
    }

    // The value of the first parameter of that name, unquoted; null when the challenge has none. A header that takes longer
    // than the timeout to read names no parameter: the detection never throws in an error handler.
    private static string? GetChallengeParameter(string? challenge, string name)
    {
        if (challenge is null)
        {
            return null;
        }

        try
        {
            foreach (Match match in AuthParameter.Matches(challenge))
            {
                if (string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    return match.Groups["quoted"].Success
                        ? QuotedPair.Replace(match.Groups["quoted"].Value, "$1")
                        : match.Groups["token"].Value;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return null;
    }

    // A window no TimeSpan holds is no window: the detection never throws in an error handler, whatever a proxy sends.
    private static TimeSpan? ToWindow(long seconds)
    {
        return seconds >= 0 && seconds <= MaxWindowSeconds ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static TimeSpan? GetBodyMaxAge(JObject? body)
    {
        return body?["Details"] is JObject details
            && details["MaxAge"] is JValue { Type: JTokenType.Integer, Value: long seconds }
            ? ToWindow(seconds)
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
