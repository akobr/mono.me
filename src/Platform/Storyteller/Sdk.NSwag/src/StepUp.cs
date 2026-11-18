using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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

    // The schemes of OAuth access tokens (RFC 6750, RFC 9449), the only ones whose error names the step-up (RFC 9470).
    private static readonly string[] OAuthSchemes = ["Bearer", "DPoP"];

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

    // A 401 that names the step-up by its error code (the typed body or the JSON text) or by its OAuth challenge. maxAge is
    // the window the API names, from the challenge or the body, or null when neither says.
    public static bool IsStepUpRequired(this ApiException exception, out TimeSpan? maxAge)
    {
        ArgumentNullException.ThrowIfNull(exception);
        maxAge = null;

        if (exception.StatusCode != 401)
        {
            return false;
        }

        var body = ReadBody(exception);
        var challenges = GetChallenges(exception.Headers);
        var byCode = string.Equals(GetErrorCode(exception, body), ErrorCode, StringComparison.Ordinal);

        // The OAuth challenge that names the step-up; with none, the only challenge there is, when it is an OAuth one (a
        // step-up named by its code). The error of another scheme (Basic of a gateway) names no step-up of the API.
        var stepUpChallenge = challenges.FirstOrDefault(challenge =>
            IsOAuth(challenge)
            && challenge.Parameters.TryGetValue("error", out var error)
            && string.Equals(error, ChallengeError, StringComparison.OrdinalIgnoreCase));

        if (!byCode && stepUpChallenge is null)
        {
            return false;
        }

        var only = challenges.Count == 1 && IsOAuth(challenges[0]) ? challenges[0] : null;
        maxAge = GetChallengeMaxAge(stepUpChallenge ?? only) ?? GetBodyMaxAge(body) ?? GetTypedMaxAge(exception);
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

    private static bool IsOAuth(Challenge challenge)
    {
        return OAuthSchemes.Contains(challenge.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    // The challenges of every WWW-Authenticate line (RFC 9110, 11.6.1), each with its scheme and its parameters (the first
    // value of a name, unquoted), the challenges without parameters included. A token followed by "=" and a value is a
    // parameter of the current challenge; any other token starts a challenge, and a token68 after its scheme is skipped.
    // A quoted value is read whole, so a parameter named inside another one's value (error_description) is never read.
    // The parser is linear and never throws in an error handler, whatever a proxy sends.
    private static List<Challenge> GetChallenges(IReadOnlyDictionary<string, IEnumerable<string>>? headers)
    {
        var challenges = new List<Challenge>();

        if (headers is null)
        {
            return challenges;
        }

        var lines = headers
            .Where(header => string.Equals(header.Key, WwwAuthenticateHeader, StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value ?? []);

        foreach (var line in lines)
        {
            if (line is not null)
            {
                ReadChallenges(line, challenges);
            }
        }

        return challenges;
    }

    private static void ReadChallenges(string line, List<Challenge> challenges)
    {
        Challenge? current = null;
        var position = 0;

        while (true)
        {
            position = Skip(line, position, ", \t");

            if (position >= line.Length)
            {
                return;
            }

            var tokenEnd = SkipWhile(line, position, IsTokenChar);

            if (tokenEnd == position)
            {
                // A stray quoted string or another character is skipped.
                position = line[position] == '"' ? ReadQuoted(line, position, out _) : position + 1;
                continue;
            }

            var name = line[position..tokenEnd];
            var equals = Skip(line, tokenEnd, " \t");

            if (current is not null
                && equals < line.Length
                && line[equals] == '='
                && TryReadValue(line, Skip(line, equals + 1, " \t"), out var value, out var valueEnd))
            {
                current.Parameters.TryAdd(name, value);
                position = valueEnd;
                continue;
            }

            current = new Challenge(name);
            challenges.Add(current);
            position = SkipToken68(line, tokenEnd);
        }
    }

    // A token or a quoted string; nothing, a list separator or another "=" (of a token68) is no value.
    private static bool TryReadValue(string line, int position, out string value, out int end)
    {
        value = string.Empty;
        end = position;

        if (position >= line.Length)
        {
            return false;
        }

        if (line[position] == '"')
        {
            end = ReadQuoted(line, position, out value);
            return true;
        }

        end = SkipWhile(line, position, character => character is not (' ' or '\t' or ',' or '"'));

        if (end == position || line[position] == '=')
        {
            return false;
        }

        value = line[position..end];
        return true;
    }

    // From the opening quote: the unquoted text, and the position after the closing quote (or the end of the line).
    private static int ReadQuoted(string line, int position, out string value)
    {
        var text = new StringBuilder();
        var index = position + 1;

        while (index < line.Length && line[index] != '"')
        {
            if (line[index] == '\\' && index + 1 < line.Length)
            {
                index++;
            }

            text.Append(line[index]);
            index++;
        }

        value = text.ToString();
        return Math.Min(index + 1, line.Length);
    }

    // After a scheme: a token68 ("YIIabc==") runs up to a comma or the end of the line, a parameter name does not.
    private static int SkipToken68(string line, int schemeEnd)
    {
        var start = Skip(line, schemeEnd, " \t");
        var end = SkipWhile(line, start, character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/');

        if (end == start)
        {
            return schemeEnd;
        }

        end = SkipWhile(line, end, character => character == '=');
        var next = Skip(line, end, " \t");

        return next >= line.Length || line[next] == ',' ? next : schemeEnd;
    }

    private static int Skip(string line, int position, string characters)
    {
        return SkipWhile(line, position, character => characters.Contains(character, StringComparison.Ordinal));
    }

    private static int SkipWhile(string line, int position, Func<char, bool> predicate)
    {
        while (position < line.Length && predicate(line[position]))
        {
            position++;
        }

        return position;
    }

    // tchar of RFC 9110, 5.6.2.
    private static bool IsTokenChar(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character, StringComparison.Ordinal);
    }

    private static TimeSpan? GetChallengeMaxAge(Challenge? challenge)
    {
        if (challenge is null || !challenge.Parameters.TryGetValue("max_age", out var value))
        {
            return null;
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? ToWindow(seconds) : null;
    }

    // A window no TimeSpan holds is no window: the detection never throws in an error handler, whatever a proxy sends.
    private static TimeSpan? ToWindow(long seconds)
    {
        return seconds >= 0 && seconds <= MaxWindowSeconds ? TimeSpan.FromSeconds(seconds) : null;
    }

    // An integer of any CLR type: parsed JSON holds an Int64, a typed Details (new { MaxAge = 600 }) an Int32. A string, a
    // fraction or a number no Int64 holds (a BigInteger) gives no window.
    private static TimeSpan? GetBodyMaxAge(JObject? body)
    {
        if (body?["Details"] is not JObject details || details["MaxAge"] is not JValue { Type: JTokenType.Integer, Value: { } value })
        {
            return null;
        }

        long? seconds = value switch
        {
            long number => number,
            int number => number,
            short number => number,
            sbyte number => number,
            byte number => number,
            ushort number => number,
            uint number => number,
            ulong number when number <= long.MaxValue => (long)number,
            _ => null,
        };

        return seconds is { } window ? ToWindow(window) : null;
    }

    private sealed class Challenge(string scheme)
    {
        public string Scheme { get; } = scheme;

        public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
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
