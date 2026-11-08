using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// What the transfer, policy, client encryption and audit tests of the key/value store share: the documents of
// kv export and kv import (2s.key-values.v1, .env lines), the RSA keys of the recipients, the ids sform prints for them,
// the rows and pages of kv audit, and the project of an exact role (Target:RoleProjects).
internal static partial class KeyValuesSupportB
{
    // The format of a key/value document (KeyValueFormats.DocumentFormat of the CLI).
    public const string DocumentFormat = "2s.key-values.v1";

    // The operations of kv audit --operation (KeyValueAuditEventOperation), as the catalogue writes them.
    public static readonly IReadOnlyList<string> AuditOperations =
    [
        "Reveal", "Export", "BindingRead", "Create", "Update", "Delete", "Restore", "PermanentDelete", "Purge", "Reprotect",
        "Import", "PolicyChange", "Rotate", "Rewrap", "ByokVerify", "ByokUnavailable", "RecipientAdded", "RecipientChanged",
        "RecipientRemoved",
    ];

    // An RSA key pair of a recipient: the public key as PEM (SubjectPublicKeyInfo) and as a JWK, the private key as PEM
    // (PKCS#8), what kv recipients add, kv get --private-key and kv reencrypt --private-key take.
    public static RecipientKey NewRecipientKey(int bits = 3072)
    {
        using var rsa = RSA.Create(bits);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var jwk = new JsonObject
        {
            ["kty"] = "RSA",
            ["n"] = Base64Url.EncodeToString(parameters.Modulus!),
            ["e"] = Base64Url.EncodeToString(parameters.Exponent!),
        };

        return new RecipientKey(rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), jwk.ToJsonString(), bits);
    }

    // The id of "Recipient '<id>' (<name>) added." of kv recipients add.
    public static string RecipientIdOf(SformResult added)
    {
        ArgumentNullException.ThrowIfNull(added);
        var match = RecipientAddedPattern().Match(added.PlainStdOut);
        return match.Success ? match.Groups["id"].Value : throw added.Fail("printed no \"Recipient '<id>' (<name>) added.\"");
    }

    // The rows of the table of kv audit (Time, Operation, Key, Level, Actor, Channel, Detail): each line that starts with
    // its time ("yyyy-MM-dd HH:mm", local), with the operation that follows it. "No audit events." has none.
    public static IReadOnlyList<AuditRow> AuditRows(SformResult audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        var rows = new List<AuditRow>();
        foreach (var line in audit.Lines)
        {
            var match = AuditRowPattern().Match(line);
            if (match.Success)
            {
                var time = DateTime.ParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                rows.Add(new AuditRow(line.Trim(), match.Groups["operation"].Value, time));
            }
        }

        return rows;
    }

    // The continuation token of "More events follow; next page with -k <token>" (kv audit) or "More keys follow; next
    // page with -k <token>" (kv); null on the last page.
    public static string? NextPageToken(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var match = NextPagePattern().Match(result.PlainStdOut);
        return match.Success ? match.Groups["token"].Value.Trim() : null;
    }

    // The KEY=VALUE lines of .env text (kv export --format env, on the standard output with its margin or in a file).
    public static IReadOnlyDictionary<string, string> EnvEntries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (line.Length == 0 || line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            var value = line[(separator + 1)..];
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            entries[line[..separator]] = value;
        }

        return entries;
    }

    // A key as an .env name, as the CLI writes it (KeyValueFormats.ToEnvKey): '.' becomes "__", upper case.
    public static string EnvName(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Replace(".", "__", StringComparison.Ordinal).ToUpperInvariant();
    }

    // The Items of a key/value document by key (the json of kv export, a file of -o).
    public static IReadOnlyDictionary<string, JsonNode> ItemsOf(JsonNode? document)
    {
        var items = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var item in document?["Items"] as JsonArray ?? [])
        {
            if (item?["Key"]?.GetValue<string>() is { } key)
            {
                items[key] = item;
            }
        }

        return items;
    }

    // A key/value document (2s.key-values.v1) of the items, for kv import: (key, level as the API names it, value).
    public static string Document(params (string Key, string Protection, string Value)[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var array = new JsonArray();
        foreach (var (key, protection, value) in items)
        {
            array.Add(new JsonObject { ["Key"] = key, ["Protection"] = protection, ["Value"] = value });
        }

        return new JsonObject { ["Format"] = DocumentFormat, ["Items"] = array }.ToJsonString();
    }

    // The arguments with only -p of the project: the commands of the project (kv audit, where -v filters the events by
    // view, and every view's events are listed without it).
    public static string[] WithProject(string projectKey, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentNullException.ThrowIfNull(arguments);
        return [.. arguments, "-p", projectKey];
    }

    // -p <org>.<project> -v <view> of the project Target:RoleProjects maps the role to (where every pool user holds
    // exactly that role, e.g. <contributor project> of the catalogue); null when the role has no entry.
    public static string[]? RoleProjectArguments(CliTarget target, Role role)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.RoleProjectOf(role) is not { } mapped)
        {
            return null;
        }

        var parts = mapped.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return ["-p", $"{parts[0]}.{parts[1]}", "-v", parts.Length > 2 ? parts[2] : "default"];
    }

    [GeneratedRegex(@"Recipient '(?<id>[^']+)' \(", RegexOptions.CultureInvariant)]
    private static partial Regex RecipientAddedPattern();

    [GeneratedRegex(@"^\s*(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2})\s+(?<operation>[A-Za-z]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AuditRowPattern();

    [GeneratedRegex(@"next page with -k (?<token>\S.*?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NextPagePattern();

    // A recipient's RSA key pair (KeyValuesSupportB.NewRecipientKey).
    public sealed record RecipientKey(string PublicPem, string PrivatePem, string PublicJwk, int Bits);

    // A row of kv audit: the line, its operation and its time (local, to the minute).
    public sealed record AuditRow(string Line, string Operation, DateTime Time);

    // The target's customer key for the cases of the Byok feature (KV-POL-05, KV-POL-07): KeyValues:ByokKeyUri, the
    // versionless URI of a key in an Azure Key Vault the platform's application may wrap and unwrap with, and
    // KeyValues:ByokTenantId, the Entra tenant id of the vault. Read only when Target:Features lists Byok.
    public sealed class KeyValueByokSettings
    {
        public const string Section = "KeyValues";

        public string? ByokKeyUri { get; set; }

        public string? ByokTenantId { get; set; }
    }
}
