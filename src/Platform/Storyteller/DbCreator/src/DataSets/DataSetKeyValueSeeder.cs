using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// Seeds the pairs of a set into its target through the key/value import, as the System caller (both secret rights,
// the System channel), with the rules of the rest of a set: what exists stays and what is missing is created. The
// import's Merge would update a pair whose value differs and restore a deleted one, so each chunk is planned first
// with a dry run of the import, which also validates it with the project's policy, and then only the missing pairs
// are written. Writes nothing to the console; the result and the items tell what happened.
public sealed class DataSetKeyValueSeeder
{
    // The outcomes of a seed, not errors of the API: a pair that exists with another value, level or content type,
    // and a pair that is soft-deleted in the view. Neither is changed.
    public const string KeyValueExists = "KeyValueExists";
    public const string KeyValueDeleted = "KeyValueDeleted";

    // The most items one import takes (CosmosKeyValueService).
    public const int ChunkSize = 1_000;

    private readonly IKeyValueService _keyValues;
    private readonly HashSet<KeyValueProtection> _configured;
    private readonly KeyValueOptions _options;
    private readonly ILogger<DataSetKeyValueSeeder> _logger;

    public DataSetKeyValueSeeder(
        IKeyValueService keyValues,
        IEnumerable<IKeyEncryptionProvider> providers,
        IOptions<KeyValueOptions> options,
        ILogger<DataSetKeyValueSeeder> logger)
    {
        _keyValues = keyValues;
        _configured = providers.Select(provider => provider.Protection).ToHashSet();
        _options = options.Value;
        _logger = logger;
    }

    // Public needs nothing; another level needs its key-encryption provider (AddKeyVaultKeyProtection).
    public bool IsConfigured(KeyValueProtection level)
    {
        return level == KeyValueProtection.Public || _configured.Contains(level);
    }

    // DataSetException for pairs of a level the tool cannot encrypt: Standard without a Standard vault. The other
    // levels a seed refuses are refused when the file is read (DataSetKeyValues).
    public void EnsureSeedable(DataSet set, DataSetKeyValues pairs)
    {
        var standard = pairs.Count(KeyValueProtection.Standard);

        if (standard == 0 || IsConfigured(KeyValueProtection.Standard))
        {
            return;
        }

        var vault = _options.Protection.Standard.VaultName;
        throw new DataSetException(
            $"The set '{set.Name}' has {standard} Standard {(standard == 1 ? "pair" : "pairs")} in {DataSetKeyValues.FileName}, and DbCreator has no Standard vault to encrypt them; nothing was written. "
            + $"Set AzureKeyVaults__{vault} to the URI of the API's Standard vault, for an identity with the roles the API's identity has on it (Key Vault Crypto Officer and Key Vault Crypto Service Encryption User): "
            + "a Release build (the dotnet tool) signs in with a managed identity or AZURE_TENANT_ID, AZURE_CLIENT_ID and AZURE_CLIENT_SECRET; "
            + "a Debug build (dotnet run) also takes Lowkey Vault, with KeyValues__Protection__Emulator=true.");
    }

    // The pairs into the target in chunks of the import, in the order of the file: each chunk planned with a dry run,
    // then (unless dryRun) its missing pairs written. Adds an item per outcome but Created and Unchanged to items.
    // Failures of a pair are outcomes; a failure of the storage ends the seed.
    public async Task<DataSetKeyValuesResult> SeedAsync(
        DataSetTarget target,
        DataSetKeyValues pairs,
        bool dryRun,
        List<DataSetSeedItem> items,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new PairOutcome?[pairs.Items.Count];
        var caller = KeyValueCaller.System();
        HashSet<string>? deleted = null;

        for (var start = 0; start < pairs.Items.Count; start += ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = Enumerable.Range(start, Math.Min(ChunkSize, pairs.Items.Count - start)).ToList();
            var missing = new List<int>();

            foreach (var (index, action) in await PlanAsync(target, pairs, chunk, outcomes, caller))
            {
                switch (action)
                {
                    case KeyValueImportAction.Unchanged:
                        outcomes[index] = new PairOutcome(ViewImportOutcome.Unchanged);
                        break;

                    case KeyValueImportAction.Update:
                        outcomes[index] = new PairOutcome(
                            ViewImportOutcome.Skipped,
                            KeyValueExists,
                            "The pair exists with another value, level or content type; a seed never changes it.");
                        break;

                    case KeyValueImportAction.Create:
                        // the plan restores a deleted key as it creates a missing one; only the listing tells them apart
                        deleted ??= await GetKeysAsync(target, KeyValueState.Deleted);

                        if (deleted.Contains(KeyValueRules.Normalize(pairs.Items[index].Key)))
                        {
                            outcomes[index] = new PairOutcome(
                                ViewImportOutcome.Skipped,
                                KeyValueDeleted,
                                "The pair is deleted in the view; a seed never restores it.");
                        }
                        else
                        {
                            missing.Add(index);
                        }

                        break;

                    default:
                        // Merge plans no delete
                        throw new InvalidOperationException($"The import planned {action} for the pair '{pairs.Items[index].Key}' of a merge.");
                }
            }

            if (dryRun)
            {
                foreach (var index in missing)
                {
                    outcomes[index] = new PairOutcome(ViewImportOutcome.Created);
                }
            }
            else if (missing.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteAsync(target, pairs, missing, outcomes, caller);
            }
        }

        return Complete(pairs, outcomes, items, target, dryRun);
    }

    private static KeyValueDocument Document(DataSetKeyValues pairs, IEnumerable<int> indexes)
    {
        return new KeyValueDocument { Items = indexes.Select(index => pairs.Items[index]).ToList() };
    }

    // A failure of a pair, not of the storage: an error of the platform, a ciphertext that does not decrypt, and the
    // vault's errors, also as Azure.Core's retry policy reports a vault it cannot reach (connection refused, a name that
    // does not resolve, a timeout): an AggregateException of the failed tries.
    private static bool IsPairFailure(Exception exception)
    {
        return exception is StorytellerException or KeyValueIntegrityException or RequestFailedException or AuthenticationFailedException
            || IsVaultRetryFailure(exception);
    }

    private static bool IsVaultRetryFailure(Exception exception)
    {
        if (exception is not AggregateException aggregate)
        {
            return false;
        }

        var tries = aggregate.Flatten().InnerExceptions;
        return tries.Count > 0
            && tries.All(inner => inner is RequestFailedException or AuthenticationFailedException or IOException or OperationCanceledException);
    }

    private static PairOutcome Failed(Exception exception)
    {
        if (IsVaultRetryFailure(exception))
        {
            var tries = ((AggregateException)exception).Flatten().InnerExceptions;
            return new PairOutcome(
                ViewImportOutcome.Failed,
                ErrorCodes.KeyEncryptionUnavailable,
                $"The key vault failed after {tries.Count} {(tries.Count == 1 ? "try" : "tries")}: {tries[^1].Message}");
        }

        return exception switch
        {
            StorytellerException storyteller => new PairOutcome(ViewImportOutcome.Failed, storyteller.ErrorCode, storyteller.Message),
            KeyValueIntegrityException => new PairOutcome(ViewImportOutcome.Failed, ErrorCodes.KeyValueIntegrity, exception.Message),
            _ => new PairOutcome(ViewImportOutcome.Failed, ErrorCodes.KeyEncryptionUnavailable, $"The key vault failed: {exception.Message}"),
        };
    }

    // The items of the chunk by their normalized key; the keys are unique ignoring case (DataSetKeyValues).
    private static Dictionary<string, int> ByKey(DataSetKeyValues pairs, IEnumerable<int> indexes)
    {
        return indexes.ToDictionary(index => KeyValueRules.Normalize(pairs.Items[index].Key), StringComparer.Ordinal);
    }

    // The items an invalid import lists, failed with their codes; returns their indexes.
    private static HashSet<int> FailListed(KeyValueImportInvalidException exception, DataSetKeyValues pairs, IReadOnlyList<int> indexes, PairOutcome?[] outcomes)
    {
        var byKey = ByKey(pairs, indexes);
        var failed = new HashSet<int>();

        foreach (var item in exception.Items)
        {
            if (byKey.TryGetValue(KeyValueRules.Normalize(item.Key), out var index) && failed.Add(index))
            {
                outcomes[index] = new PairOutcome(ViewImportOutcome.Failed, item.ErrorCode ?? exception.ErrorCode, item.Message ?? exception.Message);
            }
        }

        return failed;
    }

    private static void FailAll(IEnumerable<int> indexes, PairOutcome?[] outcomes, PairOutcome outcome)
    {
        foreach (var index in indexes)
        {
            outcomes[index] = outcome;
        }
    }

    // The counts, and an item per listed outcome in the order of the file.
    private DataSetKeyValuesResult Complete(DataSetKeyValues pairs, PairOutcome?[] outcomes, List<DataSetSeedItem> items, DataSetTarget target, bool dryRun)
    {
        var summary = new ViewImportSummary();

        for (var index = 0; index < pairs.Items.Count; index++)
        {
            var item = pairs.Items[index];

            // every index gets an outcome; one without would be a bug of the steps above
            var outcome = outcomes[index] ?? new PairOutcome(ViewImportOutcome.Failed, ErrorCodes.ImportInvalid, "The import did not plan the pair.");

            summary = outcome.Outcome switch
            {
                ViewImportOutcome.Created => summary with { Created = summary.Created + 1 },
                ViewImportOutcome.Updated => summary with { Updated = summary.Updated + 1 },
                ViewImportOutcome.Unchanged => summary with { Unchanged = summary.Unchanged + 1 },
                ViewImportOutcome.Skipped => summary with { Skipped = summary.Skipped + 1 },
                _ => summary with { Failed = summary.Failed + 1 },
            };

            if (outcome.Outcome is not (ViewImportOutcome.Created or ViewImportOutcome.Unchanged))
            {
                items.Add(new DataSetSeedItem
                {
                    Path = DataSetKeyValues.FileName,
                    KeyValueKey = item.Key,
                    Protection = item.Protection,
                    Outcome = outcome.Outcome,
                    ErrorCode = outcome.ErrorCode,
                    Message = outcome.Message,
                });
            }
        }

        _logger.LogInformation(
            "{Mode} {Pairs} pairs into {Target}: {Created} created, {Unchanged} unchanged, {Skipped} skipped, {Failed} failed.",
            dryRun ? "Planned" : "Seeded",
            pairs.Items.Count,
            target,
            summary.Created,
            summary.Unchanged,
            summary.Skipped,
            summary.Failed);

        return new DataSetKeyValuesResult(pairs.Items.Count, pairs.Levels, summary);
    }

    // The plan of the import for the chunk (a dry run): its action per index. The items an invalid plan lists fail, and
    // the rest is planned again without them; a failure of the whole call fails the chunk.
    private async Task<List<(int Index, KeyValueImportAction Action)>> PlanAsync(
        DataSetTarget target,
        DataSetKeyValues pairs,
        IReadOnlyList<int> chunk,
        PairOutcome?[] outcomes,
        KeyValueCaller caller)
    {
        var pending = chunk.ToList();

        while (pending.Count > 0)
        {
            try
            {
                var plan = await _keyValues.ImportAsync(
                    target.Organization,
                    target.Project,
                    target.View,
                    Document(pairs, pending),
                    new KeyValueImportOptions { Mode = KeyValueImportMode.Merge, DryRun = true },
                    caller);
                var byKey = ByKey(pairs, pending);

                return plan.Items
                    .Where(item => byKey.ContainsKey(KeyValueRules.Normalize(item.Key)))
                    .Select(item => (byKey[KeyValueRules.Normalize(item.Key)], item.Action))
                    .ToList();
            }
            catch (KeyValueImportInvalidException exception)
            {
                var refused = FailListed(exception, pairs, pending, outcomes);

                if (refused.Count == 0)
                {
                    FailAll(pending, outcomes, Failed(exception));
                    return [];
                }

                _logger.LogInformation("The import refused {Count} pairs of {Target}; planning the rest again.", refused.Count, target);
                pending = pending.Where(index => !refused.Contains(index)).ToList();
            }
            catch (Exception exception) when (IsPairFailure(exception))
            {
                _logger.LogWarning(exception, "The plan of {Count} pairs of {Target} failed.", pending.Count, target);
                FailAll(pending, outcomes, Failed(exception));
                return [];
            }
        }

        return [];
    }

    // The missing pairs of a chunk in one import. Create is Created; Update and Unchanged happen only when another
    // writer created the key after the plan, and are reported as they are.
    private async Task WriteAsync(DataSetTarget target, DataSetKeyValues pairs, IReadOnlyList<int> missing, PairOutcome?[] outcomes, KeyValueCaller caller)
    {
        try
        {
            var result = await _keyValues.ImportAsync(
                target.Organization,
                target.Project,
                target.View,
                Document(pairs, missing),
                new KeyValueImportOptions { Mode = KeyValueImportMode.Merge },
                caller);
            var byKey = ByKey(pairs, missing);

            foreach (var item in result.Items)
            {
                if (!byKey.TryGetValue(KeyValueRules.Normalize(item.Key), out var index))
                {
                    continue;
                }

                outcomes[index] = item switch
                {
                    { ErrorCode: not null } => new PairOutcome(ViewImportOutcome.Failed, item.ErrorCode, item.Message),
                    { Action: KeyValueImportAction.Create } => new PairOutcome(ViewImportOutcome.Created),
                    { Action: KeyValueImportAction.Unchanged } => new PairOutcome(ViewImportOutcome.Unchanged),
                    _ => new PairOutcome(
                        ViewImportOutcome.Updated,
                        Message: "Another writer created the pair after the seed planned it, and the import wrote the set's value over it."),
                };
            }
        }
        catch (KeyValueImportInvalidException exception)
        {
            // the state changed since the plan; the import wrote nothing
            var refused = FailListed(exception, pairs, missing, outcomes);
            FailAll(missing.Where(index => !refused.Contains(index)), outcomes, Failed(exception));
        }
        catch (Exception exception) when (IsPairFailure(exception))
        {
            // the import commits in batches of 50 and encrypts per batch, so a failure of a later batch leaves the
            // earlier ones stored; the listing (no vault) tells them, as the pairs planned missing that are active now
            _logger.LogWarning(exception, "The write of {Count} pairs of {Target} failed.", missing.Count, target);
            var active = await GetKeysAsync(target, KeyValueState.Active);
            var failed = Failed(exception);

            foreach (var index in missing)
            {
                outcomes[index] = active.Contains(KeyValueRules.Normalize(pairs.Items[index].Key))
                    ? new PairOutcome(ViewImportOutcome.Created)
                    : failed;
            }
        }
    }

    // The normalized keys of the pairs of the view in the state (active or soft-deleted), every page.
    private async Task<HashSet<string>> GetKeysAsync(DataSetTarget target, KeyValueState state)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;

        do
        {
            var page = await _keyValues.ListAsync(
                target.Organization,
                target.Project,
                target.View,
                new KeyValueListRequest { State = state, ContinuationToken = continuation });

            foreach (var summary in page.Items)
            {
                keys.Add(KeyValueRules.Normalize(summary.Key));
            }

            continuation = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuation));

        return keys;
    }

    private sealed record class PairOutcome(ViewImportOutcome Outcome, string? ErrorCode = null, string? Message = null);
}
