using System.ComponentModel;
using System.Globalization;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The values store (key/value pairs). None of these tools returns a secret value: reads never reveal, and the diff of
// a secret, which the API returns as plaintext without reveal, is refused before it is asked for. Not exposed: export
// and import, BYOK setup and verification, key rotation and recipients.
[McpServerToolType]
public sealed class KeyValueTools(Sdk.IKeyValuesApiClient keyValues)
{
    public const int MaxBulkKeys = 100;

    private static readonly string[] States = ["Active", "Deleted", "All"];

    [McpServerTool(Name = "list_key_values", Title = "List key/value pairs", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the key/value pairs of a view, ordered by key: Key, Protection, ContentType, Version, Modified and the "
        + "deletion state. Secret values are never listed; includeValues adds the values of public pairs. Needs Reader on "
        + "the project.")]
    public async Task<CallToolResult> ListKeyValues(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Only keys starting with this prefix (case-insensitive).")] string? keyPrefix = null,
        [Description("Only pairs of these protection levels: " + KeyValueText.Levels + ".")] string[]? protections = null,
        [Description("Active (default), Deleted or All.")] string? state = null,
        [Description("true adds the values of public pairs.")] bool? includeValues = null,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var cursor = HostPage.Decode(continuationToken);
        var response = await keyValues.GetKeyValuesAsync(
            organization,
            project,
            view,
            string.IsNullOrEmpty(keyPrefix) ? null : keyPrefix,
            KeyValueProtections.ToFilter(protections, nameof(protections)),
            ParseState(state),
            includeValues,
            cursor.ApiToken,
            cancellationToken);

        return ToolResults.Json(HostPage.Take(response.Items?.ToList() ?? [], response.ContinuationToken, cursor, HostPage.ClampLimit(limit)));
    }

    [McpServerTool(Name = "get_key_value", Title = "Get a key/value pair", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns one pair, or one stored version of it, without a secret value: Key, Protection, IsSecret, ContentType, "
        + "Version, Created, Modified, the deletion state and ClientEncrypted (true for Hyok: a value the client encrypted for "
        + "the project's recipients, which the platform cannot read). A public pair includes its Value; a secret never does. "
        + "Needs Reader on the project.")]
    public async Task<CallToolResult> GetKeyValue(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(KeyValueText.Key)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(KeyValueText.Version + " Omit it for the current version.")] int? version = null,
        [Description("true also finds a soft-deleted key (the current version only).")] bool? includeDeleted = null,
        CancellationToken cancellationToken = default)
    {
        var name = ToolArguments.Required(key, nameof(key));
        var pair = version is { } number
            ? await keyValues.GetKeyValueVersionAsync(organization, project, view, name, number, false, cancellationToken)
            : await keyValues.GetKeyValueAsync(organization, project, view, name, false, includeDeleted, cancellationToken);

        return ToolResults.Json(KeyValueProtections.Describe(pair));
    }

    [McpServerTool(Name = "set_key_value", Title = "Set a key/value pair", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Creates or updates a pair as a new version; the older versions are kept. protection is required when the key is new "
        + "and kept when it is omitted on an update. Read Version first (get_key_value) and pass it as expectedVersion, or 0 to "
        + "create only when the key does not exist; a mismatch is 412 KeyValueVersionMismatch. Public pairs need Contributor; "
        + "a secret level (Standard, Hsm, Byok), or a change to or from one, needs ContributorWithSecrets; a level the project "
        + "policy does not allow is 409 ProtectionNotAllowed. Hyok is refused, because a client-encrypted value is encrypted "
        + "on the person's machine by sform or the admin UI. A secret value given here is part of the conversation; to keep "
        + "it out, the person sets it with sform (sform story kv set <key> --secret, hidden input) or the admin UI. The "
        + "result never carries a secret value.")]
    public async Task<CallToolResult> SetKeyValue(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(KeyValueText.Key)] string key,
        [Description("The value: a string of at most 24,576 bytes in UTF-8; the empty string is allowed.")] string value,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Public, Standard, Hsm or Byok. Required for a new key; omit it to keep the level of an existing key.")] string? protection = null,
        [Description(
            "An optional content type of up to 255 characters, such as text/plain or application/json. "
            + "application/vnd.2s.secret-reference marks a public reference to a secret in the customer's own Key Vault "
            + "(kv://{vault}/{name}), which the platform never resolves.")]
        string? contentType = null,
        [Description("Write only when the key is at this version; 0 writes only when the key does not exist or is deleted.")] long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var level = EnumNames.ParseOptional<Sdk.KeyValueWriteProtection>(protection, nameof(protection));

        if (level == Sdk.KeyValueWriteProtection.Hyok)
        {
            throw new ToolInputException(KeyValueProtections.HyokRefusal);
        }

        var body = new Sdk.KeyValueWrite
        {
            Protection = level,
            Value = value ?? throw new ToolInputException("'value' is required; the empty string is allowed."),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            ExpectedVersion = expectedVersion,
        };

        var pair = await keyValues.SetKeyValueAsync(organization, project, view, ToolArguments.Required(key, nameof(key)), body, cancellationToken);
        return ToolResults.Json(KeyValueProtections.Describe(pair));
    }

    [McpServerTool(Name = "delete_key_value", Title = "Delete a key/value pair", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Deletes a pair. By default a soft delete: the key disappears from reads and listings and restore_key_values brings "
        + "it back within the deleted retention (90 days by default). permanent removes the key with every version for good "
        + "and needs Administrator. A public pair needs Contributor, a secret ContributorWithSecrets.")]
    public async Task<CallToolResult> DeleteKeyValue(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(KeyValueText.Key)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("true removes the key and every version for good; Administrator only.")] bool permanent = false,
        [Description("Delete only when the key is at this version.")] long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var name = ToolArguments.Required(key, nameof(key));
        await keyValues.DeleteKeyValueAsync(organization, project, view, name, permanent ? true : null, expectedVersion, cancellationToken);
        return ToolResults.Json(new { Key = name, Deleted = true, Permanent = permanent });
    }

    [McpServerTool(Name = "restore_key_values", Title = "Restore deleted key/value pairs", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Restores soft-deleted pairs to their latest version, without a new version: up to 100 keys, or the deleted keys "
        + "under a prefix (at most 1,000). " + KeyValueText.Outcomes + " Outcomes: Done, Unchanged, NotFound, Forbidden or "
        + "Failed. Public pairs need Contributor; a secret needs ContributorWithSecrets, otherwise its outcome is Forbidden.")]
    public async Task<CallToolResult> RestoreKeyValues(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(KeyValueText.Keys)] string[]? keys = null,
        [Description(KeyValueText.KeyPrefix)] string? keyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.Json(await keyValues.RestoreKeyValuesAsync(organization, project, view, Bulk(keys, keyPrefix), cancellationToken));
    }

    [McpServerTool(Name = "purge_key_values", Title = "Purge deleted key/value pairs", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Removes soft-deleted pairs with every version for good, before their retention ends: up to 100 keys, or the "
        + "deleted keys under a prefix (an empty prefix means every deleted key of the view). Active keys are not touched. "
        + "Permanent, there is no restore. " + KeyValueText.Outcomes + " Outcomes: Done, NotFound or Failed. Administrator only.")]
    public async Task<CallToolResult> PurgeKeyValues(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(KeyValueText.Keys)] string[]? keys = null,
        [Description(KeyValueText.KeyPrefix)] string? keyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.Json(await keyValues.PurgeKeyValuesAsync(organization, project, view, Bulk(keys, keyPrefix), cancellationToken));
    }

    [McpServerTool(Name = "protect_key_values", Title = "Change the protection of secrets", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Moves secrets between the server-side levels Standard, Hsm and Byok, as a new version per key; older versions keep "
        + "their level until they expire. Up to 100 keys, or every key under a prefix. Public and Hyok pairs need a new value "
        + "through a write (outcome NotAllowed). The target level must be allowed by the project policy (409 "
        + "ProtectionNotAllowed). " + KeyValueText.Outcomes + " Outcomes: Done (with the new Version), Unchanged, NotFound, "
        + "Forbidden, NotAllowed or Failed. Needs ContributorWithSecrets.")]
    public async Task<CallToolResult> ProtectKeyValues(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("The target level: Standard, Hsm or Byok.")] string protection,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(KeyValueText.Keys)] string[]? keys = null,
        [Description(KeyValueText.KeyPrefix)] string? keyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var level = EnumNames.Parse<Sdk.KeyValueBulkRequestProtection>(protection, nameof(protection));

        if (level is not (Sdk.KeyValueBulkRequestProtection.Standard or Sdk.KeyValueBulkRequestProtection.Hsm or Sdk.KeyValueBulkRequestProtection.Byok))
        {
            throw new ToolInputException(
                "'protection' is Standard, Hsm or Byok: the server moves secrets only between its own levels. A pair becomes "
                + "Public through set_key_value with a new value, and Hyok through sform or the admin UI.");
        }

        var body = Bulk(keys, keyPrefix);
        body.Protection = level;
        return ToolResults.Json(await keyValues.ProtectKeyValuesAsync(organization, project, view, body, cancellationToken));
    }

    [McpServerTool(Name = "list_key_value_versions", Title = "List the versions of a key/value pair", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the stored versions of a key, oldest first: Version, Protection, Modified (author and time), ExpirationTime and "
        + "IsCurrent; never a value. Versions expire 365 days after they were written; the current version of an active key "
        + "never expires. Needs Reader on the project.")]
    public async Task<CallToolResult> ListKeyValueVersions(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(KeyValueText.Key)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.Json(await keyValues.GetKeyValueVersionsAsync(organization, project, view, ToolArguments.Required(key, nameof(key)), cancellationToken));
    }

    [McpServerTool(Name = "diff_key_value", Title = "Compare versions of a key/value pair", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Compares two versions of a public pair as a text diff (Stats and Hunks): version with versionFrom, or with the "
        + "version before it; version 0 is the empty text. Public versions only: when either version is a secret the tool "
        + "refuses before anything is compared, because the diff of a secret carries its plaintext; client-encrypted (Hyok) "
        + "values cannot be compared at all. Needs Reader on the project.")]
    public async Task<CallToolResult> DiffKeyValue(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(KeyValueText.Key)] string key,
        [Description("The version to show, 1 or higher (list_key_value_versions).")] int version,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ToolText.VersionFrom + " 0 compares with the empty text.")] int? versionFrom = null,
        CancellationToken cancellationToken = default)
    {
        var name = ToolArguments.Required(key, nameof(key));

        if (version < 1 || versionFrom < 0)
        {
            throw new ToolInputException("'version' is 1 or higher and 'versionFrom' 0 or higher; list_key_value_versions lists the stored versions.");
        }

        // The protection of every compared version is checked first: the diff of a secret is plaintext even without reveal.
        var stored = await keyValues.GetKeyValueVersionsAsync(organization, project, view, name, cancellationToken) ?? [];

        foreach (var compared in new[] { version, versionFrom ?? version - 1 }.Where(number => number > 0).Distinct())
        {
            var item = stored.FirstOrDefault(candidate => candidate.Version == compared)
                ?? throw new ToolInputException($"Version {compared} of '{name}' is not stored (versions expire 365 days after they were written); list_key_value_versions lists the stored versions.");

            if (KeyValueProtections.IsSecret(item.Protection))
            {
                return ToolResults.Error(SecretDiffRefusal(name, compared, item.Protection), new { Key = name, Version = compared, item.Protection });
            }
        }

        var diff = versionFrom is { } from
            ? await keyValues.GetKeyValueVersionDiffCustomAsync(organization, project, view, name, version, from, null, cancellationToken)
            : await keyValues.GetKeyValueVersionDiffAsync(organization, project, view, name, version, null, cancellationToken);

        return ToolResults.Json(diff);
    }

    [McpServerTool(Name = "get_key_value_policy", Title = "Get the key/value policy", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the key/value policy of a project: AllowedProtections, DefaultSecretProtection, RequireMtlsForSecretDelivery "
        + "(machines receive secret values only over mTLS), the platform keys (Standard, Hsm), the customer key (Byok), the "
        + "Hyok Recipients, a re-wrap in progress (Rewrap) and Version. Needs Reader on the project.")]
    public async Task<CallToolResult> GetKeyValuePolicy(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        CancellationToken cancellationToken)
    {
        return ToolResults.Json(await keyValues.GetKeyValuePolicyAsync(organization, project, cancellationToken));
    }

    [McpServerTool(Name = "set_key_value_policy", Title = "Change the key/value policy", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Changes the key/value policy of a project; omitted members keep their values, and the policy keeps no history. "
        + "allowedProtections replaces the list of allowed levels (Public always stays allowed); disabling a level that keys "
        + "still carry is 409 ProtectionInUse. Enabling Hsm creates the project's HSM key, Byok needs a verified customer key "
        + "and Hyok an enabled recipient (both set up with sform or the admin UI). Turning RequireMtlsForSecretDelivery off "
        + "lets machines without a certificate receive secrets. Pass expectedVersion (Version from get_key_value_policy); a "
        + "stale one is 409 PolicyVersionMismatch. Administrator only.")]
    public async Task<CallToolResult> SetKeyValuePolicy(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("Every level writes may use: " + KeyValueText.Levels + ". Public is always allowed.")] string[]? allowedProtections = null,
        [Description("The secret level clients offer as default: Standard, Hsm, Byok or Hyok; it must be allowed.")] string? defaultSecretProtection = null,
        [Description("true: machines receive and write secret values only with an mTLS certificate.")] bool? requireMtlsForSecretDelivery = null,
        [Description("How often the platform rotates the project's HSM key, in days.")] int? hsmRotationDays = null,
        [Description("Change only when the policy is at this Version (get_key_value_policy).")] long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (allowedProtections is { Length: 0 })
        {
            throw new ToolInputException("'allowedProtections' lists every allowed level; pass [\"Public\"] to allow public pairs only, or omit it to keep the list.");
        }

        if (hsmRotationDays < 1)
        {
            throw new ToolInputException("'hsmRotationDays' is 1 or higher.");
        }

        var body = new Sdk.KeyValuePolicyUpdate
        {
            AllowedProtections = allowedProtections?
                .Select(level => EnumNames.Parse<Sdk.AllowedProtections2>(level, nameof(allowedProtections)))
                .Distinct()
                .ToList(),
            DefaultSecretProtection = EnumNames.ParseOptional<Sdk.KeyValuePolicyUpdateDefaultSecretProtection>(defaultSecretProtection, nameof(defaultSecretProtection)),
            RequireMtlsForSecretDelivery = requireMtlsForSecretDelivery,
            HsmRotationInterval = hsmRotationDays is { } days ? TimeSpan.FromDays(days).ToString("c", CultureInfo.InvariantCulture) : null,
            ExpectedVersion = expectedVersion,
        };

        if (body.AllowedProtections is null && body.DefaultSecretProtection is null && body.RequireMtlsForSecretDelivery is null && body.HsmRotationInterval is null)
        {
            throw new ToolInputException("Nothing to change; give at least one of allowedProtections, defaultSecretProtection, requireMtlsForSecretDelivery or hsmRotationDays.");
        }

        return ToolResults.Json(await keyValues.SetKeyValuePolicyAsync(organization, project, body, cancellationToken));
    }

    [McpServerTool(Name = "get_key_value_audit", Title = "Get the audit trail of secrets", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the audit trail of the project's secrets, newest first: who revealed, wrote, deleted, restored, purged, "
        + "re-protected, exported or imported secrets, changed the policy, the keys or the recipients, and the binding reads of "
        + "secrets counted per hour (BindingRead with Count). Each event has Time, View, Key, Version, Protection, Operation, "
        + "Actor, Channel and CorrelationId; never a value. Administrator only.")]
    public async Task<CallToolResult> GetKeyValueAudit(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("Only events of this view; omit it for every view and the project events.")] string? filterView = null,
        [Description("Only events of this key.")] string? key = null,
        [Description(
            "Only this operation: Reveal, Export, BindingRead, Create, Update, Delete, Restore, PermanentDelete, Purge, "
            + "Reprotect, Import, PolicyChange, Rotate, Rewrap, ByokVerify, ByokUnavailable, RecipientAdded, RecipientChanged "
            + "or RecipientRemoved.")]
        string? operation = null,
        [Description("Only events at or after this time (ISO 8601; UTC when no offset is given).")] string? from = null,
        [Description("Only events before this time (ISO 8601; UTC when no offset is given).")] string? to = null,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var cursor = HostPage.Decode(continuationToken);
        var response = await keyValues.GetKeyValueAuditAsync(
            organization,
            project,
            string.IsNullOrWhiteSpace(filterView) ? null : filterView.Trim(),
            string.IsNullOrWhiteSpace(key) ? null : key.Trim(),
            EnumNames.ParseOptional<Sdk.KeyValueAuditEventOperation>(operation, nameof(operation))?.ToString(),
            ParseTime(from, nameof(from)),
            ParseTime(to, nameof(to)),
            cursor.ApiToken,
            cancellationToken);

        return ToolResults.Json(HostPage.Take(response.Items?.ToList() ?? [], response.ContinuationToken, cursor, HostPage.ClampLimit(limit)));
    }

    [McpServerTool(Name = "get_key_value_usage", Title = "Get the key/value usage", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the key/value usage of a project per day over a range of days (the last 30 by default, at most 400): the "
        + "counters (reads.api, reads.binding, reveals, writes, kms.*), the stored keys per level and versions, their Totals "
        + "and, when the deployment has prices, EstimatedCharges. The counters are kept per project and day and written "
        + "every minute, so a crashed instance can lose the last minute. Administrator only.")]
    public async Task<CallToolResult> GetKeyValueUsage(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("The first day, yyyy-MM-dd; 30 days before 'to' by default.")] string? from = null,
        [Description("The last day, yyyy-MM-dd; today (UTC) by default.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.Json(await keyValues.GetKeyValueUsageAsync(organization, project, ParseDay(from, nameof(from)), ParseDay(to, nameof(to)), cancellationToken));
    }

    private static Sdk.KeyValueBulkRequest Bulk(string[]? keys, string? keyPrefix)
    {
        var hasKeys = keys is { Length: > 0 };

        if (hasKeys == (keyPrefix is not null))
        {
            throw new ToolInputException("Give either 'keys' (up to 100) or 'keyPrefix' (an empty string for every key of the view), not both.");
        }

        if (keys is { Length: > MaxBulkKeys })
        {
            throw new ToolInputException($"'keys' holds {keys.Length} keys; at most {MaxBulkKeys} go into one call, or use 'keyPrefix'.");
        }

        return new Sdk.KeyValueBulkRequest
        {
            Keys = hasKeys ? keys!.Select(item => ToolArguments.Required(item, nameof(keys))).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null,
            KeyPrefix = keyPrefix,
        };
    }

    private static string? ParseState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return States.FirstOrDefault(candidate => string.Equals(candidate, state.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ToolInputException($"'state' is Active, Deleted or All, not '{state}'.");
    }

    // The SDK writes the time without its offset ("s") and the API reads it as UTC, so the time is sent in UTC.
    private static DateTimeOffset? ParseTime(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time.ToUniversalTime()
            : throw new ToolInputException($"'{parameter}' is not an ISO 8601 date or time: '{value}'.");
    }

    private static string? ParseDay(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? value.Trim()
            : throw new ToolInputException($"'{parameter}' is a day as yyyy-MM-dd, not '{value}'.");
    }

    private static string SecretDiffRefusal(string key, long version, Sdk.KeyValueVersionProtection protection)
    {
        return protection == Sdk.KeyValueVersionProtection.Hyok
            ? $"Version {version} of '{key}' is client-encrypted (Hyok): the platform cannot read it, so it cannot be compared."
            : $"Version {version} of '{key}' is a secret ({protection}). diff_key_value compares public versions only, because "
                + "the diff of a secret carries its plaintext. A person with ContributorWithSecrets compares secret versions with "
                + $"sform (sform story kv diff {key}) or the admin UI.";
    }
}
