using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sentry;

namespace _42.Platform.Cli.ErrorReporting;

// What the reports of a run may show of a text. The values the customer gave sform are known (CommandLineValues), so they
// go by value; the other secrets and customer data go by shape.
// - ScrubSecrets, for the local log: the shapes of secrets (the rules of the API's SentryScrubber), then the values.
// - ScrubForSentry, for an exception message in Sentry: its first line; the shapes of secrets, quoted text, the paths and
//   queries of URLs, file paths, e-mail addresses and GUIDs; then the values; cut at 300 characters.
// - ScrubEvent, the BeforeSend of ErrorReporter: the exception messages, and what the SDK adds on its own (user,
//   server name, assembly paths, tags, extras, contexts, Exception.Data).
// The shapes go first: a value replaced inside one ("acme" of ada@acme.com, "https:" of a URL, "password" of
// Password=…) would hide the shape from its rule and leave the rest of it. A value a shape covered in part loses the
// rest too: what is left of it next to a mask (kV9# of kV9#[Filtered]) is masked with it.
// A regex that times out throws: RedactedException writes the chain without messages, and BeforeSend drops the event.
internal static class ReportScrubber
{
    public const string Mask = "***";
    public const string Filtered = "[Filtered]";
    public const string CommandTag = "command";
    public const string CommandContext = "command";
    public const string InstallationTag = "posthog_distinct_id";

    public const int MaxSentryMessageLength = 300;

    // What a shape left of a value is looked for up to this length.
    private const int MaxRemnantLength = 512;

    // A shorter value is replaced as a whole word only: "v1", "dev" or "a" would cut every other word.
    private const int MinimumSubstringLength = 4;

    // The rest of a file path: a space only while a separator follows further on the line (C:\Users\Ada B Lovelace\x.pfx
    // in …, OneDrive - Contoso\x), an apostrophe only inside a word (O'Brien).
    private const string PathRest = @"(?:[^\s'""<>|]|(?<=\p{L})'(?=\p{L})|[ ](?=[^'""<>|\r\n\\/]*[\\/]))*";

    // A value shorter than MinimumSubstringLength stands alone: no letter or digit next to it, but an escape (%22, \n) may be.
    private const string ShortValueBefore = @"(?:(?<![\p{L}\p{N}])|(?<=%[0-9A-Fa-f]{2})|(?<=\\[A-Za-z]))";
    private const string ShortValueAfter = @"(?:(?![\p{L}\p{N}])|(?=%[0-9A-Fa-f]{2})|(?=\\))";

    // The start of an absolute file path.
    private const string PathStart = @"(?:[A-Za-z]:[\\/]|\\\\|~[\\/]|/)";

    // Opaque tokens: base64 keys and secrets have at least this entropy per character; names, GUIDs and hex stay below it.
    private const double TokenEntropy = 4.2;
    private const int TokenLength = 32;

    // The value of a JSON member with one of these names is filtered (value: the member of a key/value write; protected,
    // encrypted_key, iv, ciphertext, tag: a client-encrypted value, a JWE in the JSON serialization).
    private const string SecretJsonNames =
        "value|password|secret|token|access_token|refresh_token|id_token|client_secret|clientSecret|apiKey|api_key|accessKey"
        + "|privateKey|private_key|connectionString|credentials|plaintext|protected|encrypted_key|iv|ciphertext|tag";

    // name=value in connection strings, query strings and form bodies.
    private const string SecretAssignmentNames =
        "AccountKey|SharedAccessKey|SharedAccessSignature|AccessKey|Password|Pwd|ClientSecret|client_secret|Secret|Token"
        + "|access_token|refresh_token|id_token|subject_token|actor_token|client_assertion|code_verifier|ApiKey|api_key|sig"
        + "|InstrumentationKey";

    // An object or array value with nested ones, whole (balancing groups count the brackets).
    private const string NestedObject = @"\{(?>[^{}]+|\{(?<o>)|\}(?<-o>))*(?(o)(?!))\}";
    private const string NestedArray = @"\[(?>[^\[\]]+|\[(?<a>)|\](?<-a>))*(?(a)(?!))\]";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal) { CommandTag, InstallationTag };

    private static readonly HashSet<string> AllowedContexts = new(StringComparer.Ordinal)
    {
        CommandContext,
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

    private static readonly (Regex Pattern, string Replacement)[] SecretRules =
    [
        // PEM blocks (a truncated one up to the end)
        (Create(@"-----BEGIN [A-Z0-9 ]+-----[\s\S]*?(?:-----END [A-Z0-9 ]+-----|$)"), Filtered),

        // JWTs and compact JWEs
        (Create(@"eyJ[A-Za-z0-9_-]{2,}(?:\.[A-Za-z0-9_-]*){1,4}"), Filtered),

        // an authorization scheme and a credential (a word without a digit or a symbol is prose: "Bearer token")
        (Create(@"\b(Bearer|ApiKey|Basic|Digest|Negotiate|NTLM)(\s+)(?=[^\s""',;]*[0-9._~+/=-])[^\s""',;]{8,}", RegexOptions.IgnoreCase), $"$1$2{Filtered}"),

        // Basic with any base64 of user:password, letters only too: groups of four, so prose ("Basic information") stays
        (Create(@"\b(Basic)(\s+)(?:[A-Za-z0-9+/]{4}){2,}(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?(?![A-Za-z0-9+/=])", RegexOptions.IgnoreCase), $"$1$2{Filtered}"),

        // a Storyteller API key: 2s.<base64url(organization:project:machine)>.<secret>
        (Create(@"\b2s\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{16,}"), Filtered),

        // name=value secrets of connection strings, query strings and form bodies
        (Create($@"(?<![A-Za-z0-9_])({SecretAssignmentNames})(\s*=\s*)(?:""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|[^;\s""'&<>]+)", RegexOptions.IgnoreCase), $"$1$2{Filtered}"),

        // secret JSON members, plain and JSON-escaped inside a string, with any value
        (Create($@"""({SecretJsonNames})""(\s*:\s*)(?:""(?:[^""\\]|\\.)*""|{NestedObject}|{NestedArray}|[^,}}\]\s]+)", RegexOptions.IgnoreCase), $"\"$1\"$2\"{Filtered}\""),
        (Create($@"\\""({SecretJsonNames})\\""(\s*:\s*)(?:\\""(?:(?!\\"").)*\\""|{NestedObject}|{NestedArray}|[^,}}\]\s\\]+)", RegexOptions.IgnoreCase), $"\\\"$1\\\"$2\\\"{Filtered}\\\""),
    ];

    // Quoted text, for Sentry only: what a .NET message quotes is the input ("Could not find file '…'", "'…' is not a
    // valid number"); an apostrophe inside a word ("can't") opens no quote.
    private static readonly (Regex Pattern, string Replacement)[] QuoteRules =
    [
        // a quoted file path whole: a quote in it (O'Brien, Chris' Projects\x) goes on when a letter, or a separator
        // before the next quote, follows it
        (Create($@"(?<![\p{{L}}\p{{N}}])'(?={PathStart})(?:[^'\r\n]|'(?=[\p{{L}}\p{{N}}]|[^'\r\n]*[\\/][^'\r\n]*'))*'(?![\p{{L}}\p{{N}}])"), $"'{Mask}'"),

        (Create(@"(?<![\p{L}\p{N}])'[^'\r\n]*'(?![\p{L}\p{N}])"), $"'{Mask}'"),
        (Create(@"""[^""\r\n]*"""), $"\"{Mask}\""),
        (Create(@"‘[^’\r\n]*’"), $"‘{Mask}’"),
        (Create(@"“[^”\r\n]*”"), $"“{Mask}”"),
    ];

    // A quote the first line cut off, up to the end: an opening quote (after a space or a bracket) with no other after it.
    // Only on a cut line: Newtonsoft writes a lone quote character into its messages ("Expected delimiter: \". Path …").
    private static readonly (Regex Pattern, string Replacement)[] CutQuoteRules =
    [
        (Create(@"(?<![^\s(\[{:=,])'[^'\r\n]+$"), $"'{Mask}"),
        (Create(@"(?<![^\s(\[{:=,])""[^""\r\n]+$"), $"\"{Mask}"),
    ];

    // The masks the rules write, for what a shape left of a value next to one.
    private static readonly string[] Masks = [Mask, Filtered];

    private static readonly Regex AdjacentMasks = Create(@"(?:\*\*\*|\[Filtered\])(?:\*\*\*|\[Filtered\])+");

    // A URL keeps its scheme, host and port; the user information, the path and the query go.
    private static readonly Regex Url = Create(@"\b([A-Za-z][A-Za-z0-9+.-]*://)(?:[^\s/@'""<>]*@)?([^\s/?#'""<>@]+)([^\s'""<>]*)");

    // Customer data in a message, for Sentry only, after the secrets, the quotes and the URLs.
    private static readonly (Regex Pattern, string Replacement)[] SentryRules =
    [
        // file paths: a drive, a UNC path, the home directory, or an absolute path of two or more segments
        (Create($@"\b[A-Za-z]:[\\/]{PathRest}"), Mask),
        (Create($@"(?<![\w\\])\\\\[^\s\\'""<>|]+\\{PathRest}"), Mask),
        (Create($@"(?<![\w/~.])~[\\/]{PathRest}"), Mask),
        (Create($@"(?<![\w/:.~*-])/[^\s/'""<>|]+/{PathRest}"), Mask),

        // e-mail addresses
        (Create(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}"), Mask),

        // GUIDs: the ids of tenants, clients, machines and traces
        (Create(@"\b[0-9A-Fa-f]{8}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{4}-?[0-9A-Fa-f]{12}\b"), Mask),
    ];

    // long opaque tokens, judged by their entropy
    private static readonly Regex TokenRun = Create(@"[A-Za-z0-9+/=_-]{32,}");

    public static string ScrubSecrets(string? text, IEnumerable<string> values)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return ScrubValues(text, ScrubShapesOfSecrets(text), values);
    }

    public static string ScrubForSentry(string? text, IEnumerable<string> values)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var lineEnd = text.IndexOfAny(['\r', '\n']);
        var original = lineEnd < 0 ? text : text[..lineEnd];
        text = ScrubShapesOfSecrets(original);

        foreach (var (pattern, replacement) in lineEnd < 0 ? QuoteRules : [.. QuoteRules, .. CutQuoteRules])
        {
            text = pattern.Replace(text, replacement);
        }

        text = Url.Replace(text, match => match.Groups[3].Length > 0
            ? $"{match.Groups[1].Value}{match.Groups[2].Value}/{Mask}"
            : $"{match.Groups[1].Value}{match.Groups[2].Value}");

        foreach (var (pattern, replacement) in SentryRules)
        {
            text = pattern.Replace(text, replacement);
        }

        text = ScrubValues(original, text, values).Trim();
        return text.Length <= MaxSentryMessageLength ? text : $"{text[..(MaxSentryMessageLength - 1)]}…";
    }

    // Before an event is sent. The message is ErrorReporter's own, of fixed texts and the names of the model, so it is not
    // scrubbed by value: a value that is also a command name would cut the path.
    public static SentryEvent ScrubEvent(SentryEvent sentryEvent, IReadOnlyCollection<string> values, string? installationId)
    {
        foreach (var exception in sentryEvent.SentryExceptions ?? [])
        {
            exception.Value = ScrubForSentry(exception.Value, values);

            if (exception.Mechanism is { } mechanism)
            {
                mechanism.Description = mechanism.Description is null ? null : ScrubForSentry(mechanism.Description, values);
                mechanism.HelpLink = null;

                foreach (var key in mechanism.Data.Keys.Where(key => key != "HResult").ToList())
                {
                    mechanism.Data.Remove(key);
                }

                mechanism.Meta.Clear();
            }
        }

        // The full path of an assembly is under the user's profile for an installed tool, and a build from source has its
        // paths in the PDB: the file names are enough.
        foreach (var image in sentryEvent.DebugImages ?? [])
        {
            image.CodeFile = FileNameOf(image.CodeFile);
            image.DebugFile = FileNameOf(image.DebugFile);
        }

        foreach (var frame in sentryEvent.SentryExceptions?.SelectMany(exception => exception.Stacktrace?.Frames ?? []) ?? [])
        {
            frame.AbsolutePath = null;
        }

        foreach (var tag in sentryEvent.Tags.Keys.Where(tag => !AllowedTags.Contains(tag)).ToList())
        {
            sentryEvent.UnsetTag(tag);
        }

        // The SDK cannot remove an extra; an Exception.Data key "sentry:…" would add one.
        foreach (var key in sentryEvent.Extra.Keys.ToList())
        {
            sentryEvent.SetExtra(key, Filtered);
        }

        foreach (var key in sentryEvent.Contexts.Keys.Where(key => !AllowedContexts.Contains(key)).ToList())
        {
            sentryEvent.Contexts.Remove(key);
        }

        // The SDK sets user.id to an id of its own, or the MAC address of the machine; the installation id of the usage
        // event is the only id a report carries.
        sentryEvent.User = string.IsNullOrEmpty(installationId) ? new SentryUser() : new SentryUser { Id = installationId };
        sentryEvent.Request = new SentryRequest();
        sentryEvent.ServerName = null;
        return sentryEvent;
    }

    private static string ScrubShapesOfSecrets(string text)
    {
        foreach (var (pattern, replacement) in SecretRules)
        {
            text = pattern.Replace(text, replacement);
        }

        return TokenRun.Replace(text, match => ScrubRun(match.Value));
    }

    // Longest first, so a value goes before its parts; case-insensitive, also JSON-escaped and URL-encoded. original: the text
    // before the shapes, where a value is still whole.
    private static string ScrubValues(string original, string text, IEnumerable<string> values)
    {
        var forms = values
            .SelectMany(FormsOf)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length);

        foreach (var value in forms)
        {
            if (value.Length > 1 && original.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                text = ScrubRemnants(text, value);
            }

            text = value.Length < MinimumSubstringLength
                ? Regex.Replace(text, $"{ShortValueBefore}{Regex.Escape(value)}{ShortValueAfter}", Mask, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout)
                : text.Replace(value, Mask, StringComparison.OrdinalIgnoreCase);
        }

        return AdjacentMasks.Replace(text, Mask);
    }

    // A value a shape covered in part: its start just before a mask, and its end just after one, are masked too.
    private static string ScrubRemnants(string text, string value)
    {
        foreach (var mask in Masks)
        {
            for (var at = text.IndexOf(mask, StringComparison.Ordinal); at >= 0; at = text.IndexOf(mask, at + mask.Length, StringComparison.Ordinal))
            {
                var before = LongestEnd(text[..at], value, isStart: true);
                var afterStart = at + mask.Length;
                var after = LongestEnd(text[afterStart..], value, isStart: false);

                if (before + after == 0)
                {
                    continue;
                }

                text = $"{text[..(at - before)]}{Mask}{mask}{Mask}{text[(afterStart + after)..]}";

                // The next search starts after the trailing mask just written, never on it.
                at = at - before + (2 * Mask.Length);
            }
        }

        return text;
    }

    // The length of the longest start (or end) of value, of two characters or more and shorter than value, that text ends
    // with (or starts with). An edge with a character of a mask is none: it would match the masks written before.
    private static int LongestEnd(string text, string value, bool isStart)
    {
        for (var length = Math.Min(Math.Min(value.Length - 1, text.Length), MaxRemnantLength); length >= 2; length--)
        {
            var piece = isStart ? value.AsSpan(0, length) : value.AsSpan(value.Length - length);
            var edge = isStart ? text.AsSpan(text.Length - length) : text.AsSpan(0, length);

            if (edge.IndexOfAny('*', '[', ']') < 0 && piece.Equals(edge, StringComparison.OrdinalIgnoreCase))
            {
                return length;
            }
        }

        return 0;
    }

    private static string? FileNameOf(string? path)
    {
        return path is null ? null : path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
    }

    private static IEnumerable<string> FormsOf(string value)
    {
        yield return value;

        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

        if (!string.Equals(escaped, value, StringComparison.Ordinal))
        {
            yield return escaped;
        }

        var encoded = Uri.EscapeDataString(value);

        if (!string.Equals(encoded, value, StringComparison.Ordinal))
        {
            yield return encoded;
        }
    }

    // A run that starts with '/' is a path: its segments are judged one by one, so /api/v1/… stays.
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
