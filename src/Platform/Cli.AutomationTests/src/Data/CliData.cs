using System.Security.Cryptography;
using _42.Testing.UI.Automation.Runtime.Data;

namespace _42.Platform.Cli.AutomationTests.Data;

// The CLI suite's generated data, on top of the test's SeededData, so a run's data repeats with its seed
// (Execution:Seed). Everything a test creates carries "cli-<runId>" (the CLI's tests and the run; the admin UI's tests use
// "at-"), which makes leftovers attributable; the tests delete nothing they did not create for a case that deletes. Every
// generated part starts with 4 random hex digits before its words, so names stay unique within a run after the cuts. The
// names keep the Platform's rules:
//   - organization, project and view names: 2 to 63 of a-z, 0-9 and '-' (a slug);
//   - an annotation key: <code>.<part>[.<part>…], parts without '.', '/', '\', '?', '#' or control characters, kept to
//     40 characters, so <rst> of the catalogue is e.g. rst.cli-20261108-142233-k3x9-1f2e-dolor-amet;
//   - a key-value key: dot-separated segments of letters, digits and '_' that do not start with a digit, at most 128, no
//     '-', so "cli_<runId>.<word>_<hex4>"; such a key is also a path of a binding (@cli_….word_1f2e).
public sealed class CliData
{
    public const string Prefix = "cli";

    public const string Responsibility = "rst";
    public const string Subject = "sbt";
    public const string Unit = "unt";
    public const string Context = "cnt";
    public const string Usage = "usg";
    public const string Execution = "exe";
    public const string UnitOfExecution = "uxe";

    private const int MaxNameLength = 63;
    private const int MaxPartLength = 40;
    private const int HexLength = 4;

    // The parts after the code: rst.{r}, sbt.{s}, unt.{r}.{u}, cnt.{s}.{c}, usg.{s}.{r}, exe.{s}.{r}.{c},
    // uxe.{s}.{r}.{c}.{u}.
    private static readonly IReadOnlyDictionary<string, int> PartCounts = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [Responsibility] = 1,
        [Subject] = 1,
        [Unit] = 2,
        [Context] = 2,
        [Usage] = 2,
        [Execution] = 3,
        [UnitOfExecution] = 4,
    };

    public CliData(SeededData data, string runId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        Data = data;
        RunId = runId.ToLowerInvariant();
        RunSlug = $"{Prefix}-{RunId}";
    }

    public SeededData Data { get; }

    public string RunId { get; }

    // "cli-<runId>", e.g. cli-20261108-142233-k3x9: the start of every generated name of the run.
    public string RunSlug { get; }

    // "cli-<runId>[-<kind>]-<hex4>-<word>-<word>", at most 63 characters of a-z, 0-9 and '-'.
    public string Slug(string? kind = null)
    {
        var prefix = string.IsNullOrWhiteSpace(kind) ? RunSlug : $"{RunSlug}-{kind}";
        return Cut(Data.Slug($"{prefix}-{Data.Hex(HexLength)}"), MaxNameLength);
    }

    public string ProjectName()
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

    // A key of the type code (rst, sbt, unt, cnt, usg, exe, uxe). parts: the leading parts to use, e.g. the name of an
    // existing responsibility for its unit; the rest is generated.
    public string AnnotationKey(string code = Responsibility, params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (!PartCounts.TryGetValue(code, out var count))
        {
            throw new ArgumentException($"'{code}' is not an annotation type code; use one of: {string.Join(", ", PartCounts.Keys)}.", nameof(code));
        }

        if (parts.Length > count)
        {
            throw new ArgumentException($"A '{code}' key has {count} part(s) after the code; {parts.Length} were given.", nameof(parts));
        }

        var all = parts.Concat(Enumerable.Range(0, count - parts.Length).Select(_ => AnnotationPart()));
        return $"{code}.{string.Join('.', all)}";
    }

    // A responsibility key, <rst> of the catalogue.
    public string ResponsibilityKey()
    {
        return AnnotationKey(Responsibility);
    }

    // A unit below the responsibility key (unt.{r}.{u}), <rst>.<unt> of the catalogue.
    public string UnitKeyOf(string responsibilityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responsibilityKey);
        var segments = responsibilityKey.Split('.');
        if (segments.Length != 2 || segments[0] != Responsibility)
        {
            throw new ArgumentException($"'{responsibilityKey}' is not a responsibility key.", nameof(responsibilityKey));
        }

        return AnnotationKey(Unit, segments[1]);
    }

    // One generated key part, "cli-<runId>-<hex4>-<word>-<word>" (cut to 40).
    public string AnnotationPart()
    {
        return Cut(Data.Slug($"{RunSlug}-{Data.Hex(HexLength)}"), MaxPartLength);
    }

    // "cli_<runId>.<name>" with '_' for '-': a key of the key/value store; name defaults to "<word>_<hex4>".
    public string KeyValueKey(string? name = null)
    {
        return $"{Prefix}_{RunId.Replace('-', '_')}.{name ?? $"{Data.Word()}_{Data.Hex(HexLength)}"}";
    }

    // A label, "cli-<runId>-lbl-<hex4>-<word>" (at most 40).
    public string LabelName()
    {
        return Cut(Data.Slug($"{RunSlug}-lbl-{Data.Hex(HexLength)}", words: 1), MaxPartLength);
    }

    // A plain value of a pair or a property, "<word>-<hex4>".
    public string Value()
    {
        return $"{Data.Word()}-{Data.Hex(HexLength)}";
    }

    // A secret value: random (never from the seed, so a replayed seed does not repeat a secret), 24 characters of
    // letters and digits, so the redaction (4 characters at least) always masks it.
    public static string SecretValue()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return "s" + RandomNumberGenerator.GetString(alphabet, 23);
    }

    // An environment prefix of --env-prefix, e.g. APP_1F2E_.
    public string EnvironmentPrefix()
    {
        return $"APP_{Data.Hex(HexLength).ToUpperInvariant()}_";
    }

    private static string Cut(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd('-');
    }
}
