using System.Text.RegularExpressions;

using Sentry;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// The BeforeSend and BeforeBreadcrumb filter: no secret and no customer value leaves the API in an event.
// - The request, the user and the server name are cleared; the SDK sets none of them here, the filter makes it a guarantee.
// - Only the API's own tags stay: a structured log argument, a logging scope or an Exception.Data entry becomes a tag,
//   and their values are in the (scrubbed) message anyway. Extra values are filtered, contexts kept from an allow-list.
// - Texts (messages, exception values, breadcrumbs) lose credentials, connection-string secrets, secret JSON members,
//   query strings, e-mail addresses and long opaque tokens. Names (organization, project, view, keys) stay readable.
// An exception here drops the event (the SDK's behaviour), so a regex timeout never sends an unfiltered event.
internal static class SentryScrubber
{
    public const string Filtered = "[Filtered]";
    public const string FunctionTag = "function";
    public const string FunctionContext = "function";

    // Opaque tokens: base64 keys and secrets have at least this entropy per character; names, GUIDs and hex stay below it.
    private const double TokenEntropy = 4.2;
    private const int TokenLength = 32;

    // The tags of the worker's own error entry ("Function '{functionName}', Invocation id '{invocationId}': …").
    private const string WorkerFunctionTag = "functionName";
    private const string WorkerInvocationTag = "invocationId";

    // The value of a JSON member with one of these names is filtered (Value: the member of a key/value write; protected,
    // encrypted_key, iv, ciphertext, tag: a client-encrypted value, a JWE in the JSON serialization).
    private const string SecretJsonNames =
        "value|password|secret|token|access_token|refresh_token|id_token|client_secret|clientSecret|apiKey|api_key|accessKey"
        + "|privateKey|private_key|connectionString|credentials|plaintext|protected|encrypted_key|iv|ciphertext|tag";

    // name=value in connection strings, query strings and form bodies.
    private const string SecretAssignmentNames =
        "AccountKey|SharedAccessKey|SharedAccessSignature|AccessKey|Password|Pwd|ClientSecret|client_secret|Secret|Token"
        + "|access_token|refresh_token|id_token|subject_token|actor_token|client_assertion|code_verifier|ApiKey|api_key|sig"
        + "|InstrumentationKey";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal) { FunctionTag };

    private static readonly HashSet<string> AllowedContexts = new(StringComparer.Ordinal)
    {
        FunctionContext,
        SentryEventThrottle.ContextName,
        "trace",
        "app",
        "device",
        "os",
        "runtime",
        "gpu",
        "Current Culture",
        "Current UI Culture",
        "Memory Info",
        "ThreadPool Info",
        "Dynamic Code",
    };

    private static readonly (Regex Pattern, string Replacement)[] Rules =
    [
        // 1. PEM blocks (a truncated one up to the end)
        (Create(@"-----BEGIN [A-Z0-9 ]+-----[\s\S]*?(?:-----END [A-Z0-9 ]+-----|$)"), Filtered),

        // 2. JWTs and compact JWEs
        (Create(@"eyJ[A-Za-z0-9_-]{2,}(?:\.[A-Za-z0-9_-]*){1,4}"), Filtered),

        // 3. an authorization scheme and a credential (a word without a digit or a symbol is prose: "Bearer token")
        (Create(@"\b(Bearer|ApiKey|Basic|Digest|Negotiate|NTLM)(\s+)(?=[^\s""',;]*[0-9._~+/=-])[^\s""',;]{8,}", RegexOptions.IgnoreCase), $"$1$2{Filtered}"),

        // 4. a Storyteller API key: 2s.<base64url(organization:project:machine)>.<secret>
        (Create(@"\b2s\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{16,}"), Filtered),

        // 5. name=value secrets of connection strings, query strings and form bodies
        (Create($@"(?<![A-Za-z0-9_])({SecretAssignmentNames})(\s*=\s*)[^;\s""'&<>]+", RegexOptions.IgnoreCase), $"$1$2{Filtered}"),

        // 6. secret JSON members, plain and JSON-escaped inside a string, with any value
        (Create($@"""({SecretJsonNames})""(\s*:\s*)(?:""(?:[^""\\]|\\.)*""|\{{[^{{}}]*\}}|\[[^\[\]]*\]|[^,}}\]\s]+)", RegexOptions.IgnoreCase), $"\"$1\"$2\"{Filtered}\""),
        (Create($@"\\""({SecretJsonNames})\\""(\s*:\s*)(?:\\""(?:(?!\\"").)*\\""|\{{[^{{}}]*\}}|\[[^\[\]]*\]|[^,}}\]\s\\]+)", RegexOptions.IgnoreCase), $"\\\"$1\\\"$2\\\"{Filtered}\\\""),

        // 7. query strings
        (Create(@"\?[^\s""'<>#?=&]+=[^\s""'<>#]*"), $"?{Filtered}"),

        // 8. e-mail addresses
        (Create(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}"), Filtered),
    ];

    // 9. long opaque tokens, judged by their entropy
    private static readonly Regex TokenRun = Create(@"[A-Za-z0-9+/=_-]{32,}");

    public static string? ScrubText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (pattern, replacement) in Rules)
        {
            text = pattern.Replace(text, replacement);
        }

        return TokenRun.Replace(text, match => ScrubRun(match.Value));
    }

    // Before a breadcrumb is stored: an outgoing request's breadcrumb is dropped, the others are rebuilt from scrubbed texts.
    public static Breadcrumb? ScrubBreadcrumb(Breadcrumb breadcrumb)
    {
        if (string.Equals(breadcrumb.Type, "http", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Called when the breadcrumb is added, so the new one's timestamp is the same moment. Breadcrumb keeps a null message
        // and type as they are, whatever its parameters declare.
        var data = breadcrumb.Data?.ToDictionary(pair => pair.Key, pair => ScrubText(pair.Value) ?? string.Empty);
        return new Breadcrumb(ScrubText(breadcrumb.Message)!, breadcrumb.Type!, data, breadcrumb.Category, breadcrumb.Level);
    }

    public static void SetFunction(IEventLike target, string name, string transaction, string? invocationId)
    {
        target.TransactionName = transaction;
        target.SetTag(FunctionTag, name);

        var context = new Dictionary<string, string> { ["name"] = name };

        if (!string.IsNullOrEmpty(invocationId))
        {
            context["invocation_id"] = invocationId;
        }

        target.Contexts[FunctionContext] = context;
    }

    // The worker's own error entry is written outside the middlewares (e.g. a failed IActionResult): its tags name the
    // function and the invocation, which give the transaction before the tags are removed.
    public static void CompleteFunction(SentryEvent sentryEvent)
    {
        if (sentryEvent.TransactionName is not null
            || !sentryEvent.Tags.TryGetValue(WorkerFunctionTag, out var name)
            || string.IsNullOrEmpty(name))
        {
            return;
        }

        sentryEvent.Tags.TryGetValue(WorkerInvocationTag, out var invocationId);
        SetFunction(sentryEvent, name, FunctionTransactionNames.Get(name, method: null), invocationId);
    }

    public static void ScrubEvent(SentryEvent sentryEvent)
    {
        if (sentryEvent.Message is { } message)
        {
            sentryEvent.Message = new SentryMessage
            {
                Message = ScrubText(message.Message),
                Formatted = ScrubText(message.Formatted),
            };
        }

        foreach (var exception in sentryEvent.SentryExceptions ?? [])
        {
            exception.Value = ScrubText(exception.Value);

            if (exception.Mechanism is { } mechanism)
            {
                mechanism.Description = ScrubText(mechanism.Description);
                mechanism.HelpLink = null;

                foreach (var key in mechanism.Data.Keys.Where(key => key != "HResult").ToList())
                {
                    mechanism.Data.Remove(key);
                }
            }
        }

        foreach (var tag in sentryEvent.Tags.Keys.Where(tag => !AllowedTags.Contains(tag)).ToList())
        {
            sentryEvent.UnsetTag(tag);
        }

        foreach (var key in sentryEvent.Extra.Keys.ToList())
        {
            sentryEvent.SetExtra(key, Filtered);
        }

        foreach (var key in sentryEvent.Contexts.Keys.Where(key => !AllowedContexts.Contains(key)).ToList())
        {
            sentryEvent.Contexts.Remove(key);
        }

        sentryEvent.Request = new SentryRequest();
        sentryEvent.User = new SentryUser();
        sentryEvent.ServerName = null;
    }

    // A run that starts with '/' is a path: its segments are judged one by one, so /api/v1/acme/… stays.
    private static string ScrubRun(string run)
    {
        if (run[0] != '/' && IsOpaque(run))
        {
            return Filtered;
        }

        return run.Contains('/')
            ? string.Join('/', run.Split('/').Select(segment => IsOpaque(segment) ? Filtered : segment))
            : run;
    }

    private static bool IsOpaque(string text)
    {
        return text.Length >= TokenLength && Entropy(text) >= TokenEntropy;
    }

    // Shannon entropy in bits per character.
    private static double Entropy(string text)
    {
        var counts = new Dictionary<char, int>();

        foreach (var character in text)
        {
            counts[character] = counts.GetValueOrDefault(character) + 1;
        }

        double entropy = 0;

        foreach (var count in counts.Values)
        {
            var probability = (double)count / text.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static Regex Create(string pattern, RegexOptions options = RegexOptions.None)
    {
        return new Regex(pattern, options | RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    }
}
