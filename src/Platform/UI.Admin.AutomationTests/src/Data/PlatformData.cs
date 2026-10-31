using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Data;

namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The Platform's generated data, on top of the test's SeededData, so a run's data repeats with its seed. Everything a
// test creates carries "at-<runId>" (automation test and the run), which makes leftovers attributable; nothing is
// deleted. The names keep the Platform's rules:
//   - organization, project and view names: 2 to 63 of a-z, 0-9 and '-' (NameRules), so a slug;
//   - an annotation key: <code>.<part>[.<part>…], parts without '.', '/', '\', '?', '#' or control characters, kept
//     short (40) so the key stays under 200 characters (a uxe at most 168);
//   - a key-value key: dot-separated segments of letters, digits and '_' that do not start with a digit, at most 128,
//     no '-' at all, so it starts with "at_<runId>" instead.
public sealed class PlatformData
{
    public const string Prefix = "at";

    public const string Responsibility = "rst";
    public const string Subject = "sbt";
    public const string Unit = "unt";
    public const string Context = "cnt";
    public const string Usage = "usg";
    public const string Execution = "exe";
    public const string UnitOfExecution = "uxe";

    private const int MaxNameLength = 63;
    private const int MaxPartLength = 40;
    private const int MaxDescriptionLength = 500;
    private const int MaxEmailLocalLength = 64;

    // The parts after the code, by what they name: rst.{r}, sbt.{s}, unt.{r}.{u}, cnt.{s}.{c}, usg.{s}.{r},
    // exe.{s}.{r}.{c}, uxe.{s}.{r}.{c}.{u}.
    private static readonly IReadOnlyDictionary<string, string> PartNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Responsibility] = "r",
        [Subject] = "s",
        [Unit] = "ru",
        [Context] = "sc",
        [Usage] = "sr",
        [Execution] = "src",
        [UnitOfExecution] = "srcu",
    };

    private static readonly JsonSerializerOptions DocumentOptions = new() { WriteIndented = true };

    private readonly string emailDomain;

    public PlatformData(SeededData data, string runId, string emailDomain = "example.com")
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emailDomain);

        Data = data;
        RunId = runId.ToLowerInvariant();
        RunSlug = $"{Prefix}-{RunId}";
        this.emailDomain = emailDomain;
    }

    public SeededData Data { get; }

    public string RunId { get; }

    // "at-<runId>", e.g. at-20261031-142233-k3x9: the start of every generated name of the run.
    public string RunSlug { get; }

    // "at-<runId>[-<kind>]-<word>-<word>", at most 63 characters of a-z, 0-9 and '-'.
    public string Slug(string? kind = null)
    {
        var prefix = string.IsNullOrWhiteSpace(kind) ? RunSlug : $"{RunSlug}-{kind}";
        return Cut(Data.Slug(prefix), MaxNameLength);
    }

    public string OrganizationKey()
    {
        return Slug("org");
    }

    public string ProjectKey()
    {
        return Slug("prj");
    }

    public string ViewName()
    {
        return Slug("view");
    }

    public string MachineName()
    {
        return Slug("mach");
    }

    // A key of the type code (rst, sbt, unt, cnt, usg, exe, uxe). parts: the leading parts to use, e.g. an existing
    // subject; the rest is generated, each part "at-<runId>-<word>-<word>" (cut to 40). So the key and every missing
    // ancestor Bulk create or the API creates with it (rst.{r} of an exe, cnt.{s}.{c} of an existing subject's exe)
    // carry the run slug.
    public string AnnotationKey(string code, params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var count = PartsOf(code, nameof(code)).Length;
        if (parts.Length > count)
        {
            throw new ArgumentException($"A '{code}' key has {count} part(s) after the code; {parts.Length} were given.", nameof(parts));
        }

        var all = new string[count];
        for (var index = 0; index < count; index++)
        {
            if (index < parts.Length)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(parts[index], nameof(parts));
                all[index] = parts[index];
            }
            else
            {
                all[index] = Cut(Data.Slug(RunSlug), MaxPartLength);
            }
        }

        return $"{code}.{string.Join('.', all)}";
    }

    // The key of an ancestor of the key's type made of the key's own parts, e.g. cnt.{s}.{c} of exe.{s}.{r}.{c}, the
    // context of an execution; Bulk create and the API create the missing ancestors of a key.
    public static string AncestorKey(string key, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var segments = key.Split('.');
        var names = PartsOf(segments[0], nameof(key));
        if (segments.Length != names.Length + 1)
        {
            throw new ArgumentException($"'{key}' is not a '{segments[0]}' key: it has {names.Length} part(s) after the code.", nameof(key));
        }

        var ancestor = PartsOf(code, nameof(code));
        if (ancestor.Any(name => !names.Contains(name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A '{code}' key is not an ancestor of the '{segments[0]}' key '{key}'.", nameof(code));
        }

        return $"{code}.{string.Join('.', ancestor.Select(name => segments[names.IndexOf(name, StringComparison.Ordinal) + 1]))}";
    }

    // "at_<runId>.<name>" with '_' for '-': a valid key of the values store; name defaults to "<word>_<hex4>", so a
    // retry or another test of the run gets a key of its own.
    public string KeyValueKey(string? name = null)
    {
        return $"{Prefix}_{RunId.Replace('-', '_')}.{name ?? $"{Data.Word()}_{Data.Hex(4)}"}";
    }

    // A sentence that ends with the run slug, at most maxLength (500, the Platform's limit) characters.
    public string Description(int maxLength = MaxDescriptionLength)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLength, MaxDescriptionLength);

        var suffix = $" ({RunSlug})";
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, suffix.Length + 10);
        return Data.Sentence(maxLength - suffix.Length) + suffix;
    }

    // An own configuration of an annotation without a type schema: a probe with the run slug, a count and a flag.
    public string ConfigurationDocument()
    {
        return JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["probe"] = Slug(),
                ["count"] = Data.Random.Number(1, 99),
                ["enabled"] = Data.Random.Bool(),
            },
            DocumentOptions);
    }

    // A type template, the defaults every configuration of the type starts from.
    public string TemplateDocument()
    {
        return JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["retries"] = Data.Random.Number(0, 5),
                ["mode"] = Data.Word(),
                ["owner"] = RunSlug,
            },
            DocumentOptions);
    }

    // ui-tests+<slug>@<Target:EmailDomain>: a sub-address, so the mailbox of the domain receives every invitation.
    public string Email()
    {
        const string local = "ui-tests+";
        return $"{local}{Cut(Slug(), MaxEmailLocalLength - local.Length)}@{emailDomain}";
    }

    // paramName: the argument of the public method that holds the code.
    private static string PartsOf(string code, string paramName)
    {
        return PartNames.TryGetValue(code, out var names)
            ? names
            : throw new ArgumentException($"'{code}' is not an annotation type code; use one of: {string.Join(", ", PartNames.Keys)}.", paramName);
    }

    private static string Cut(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd('-');
    }
}
