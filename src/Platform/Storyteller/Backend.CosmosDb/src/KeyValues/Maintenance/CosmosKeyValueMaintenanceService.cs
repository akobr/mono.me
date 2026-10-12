using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.KeyValues;

// Re-wraps move the data keys of stored envelopes to the current key version after a rotation; the ciphertext
// itself does not change. The daily checks notice new key versions and the health of the customer's key.
public class CosmosKeyValueMaintenanceService : IKeyValueMaintenanceService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);

    private readonly IKeyValuePolicyStore _policies;
    private readonly IReadOnlyDictionary<KeyValueProtection, IKeyEncryptionProvider> _providers;
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IKeyValueAuditLog? _audit;
    private readonly KeyValueOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CosmosKeyValueMaintenanceService> _logger;

    public CosmosKeyValueMaintenanceService(
        IKeyValuePolicyStore policies,
        IEnumerable<IKeyEncryptionProvider> providers,
        IContainerRepositoryProvider repositoryProvider,
        IOptions<KeyValueOptions> options,
        IKeyValueAuditLog? audit = null,
        TimeProvider? timeProvider = null,
        ILogger<CosmosKeyValueMaintenanceService>? logger = null)
    {
        _policies = policies;
        _providers = providers.ToDictionary(provider => provider.Protection);
        _repositoryProvider = repositoryProvider;
        _audit = audit;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CosmosKeyValueMaintenanceService>.Instance;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (organization, project, _) in await _policies.ListAsync(cancellationToken))
        {
            try
            {
                var policy = await CheckKeysAsync(organization, project, force: false, cancellationToken);

                if (policy.Rewrap is { CompletedAt: null } pending)
                {
                    await RewrapAsync(organization, project, pending.Protection, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "The key/value maintenance of {Organization}.{Project} failed", organization, project);
            }
        }
    }

    public async Task<KeyValuePolicy> CheckKeysAsync(string organization, string project, bool force = false, CancellationToken cancellationToken = default)
    {
        var policy = await _policies.GetAsync(organization, project, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var lastCheck = new[] { policy.Standard?.CheckedAt, policy.Hsm?.Key?.CheckedAt, policy.Byok?.LastCheckedAt }.Max();

        if (!force && lastCheck is not null && now - lastCheck.Value < CheckInterval)
        {
            return policy;
        }

        var scope = new KeyValueScope(organization, project);
        var updated = policy;

        foreach (var level in new[] { KeyValueProtection.Standard, KeyValueProtection.Hsm })
        {
            var known = level == KeyValueProtection.Standard ? updated.Standard : updated.Hsm?.Key;

            if (!updated.AllowedProtections.Contains(level) || !_providers.TryGetValue(level, out var provider))
            {
                continue;
            }

            // the Standard key is free and is made sure of; an HSM key costs money and is only checked once it exists
            if (known is null && level == KeyValueProtection.Hsm)
            {
                continue;
            }

            var info = known is null
                ? await provider.EnsureKeyAsync(scope, updated, cancellationToken)
                : await provider.GetKeyInfoAsync(scope, updated, cancellationToken);
            updated = CosmosKeyValuePolicyService.WithKeyInfo(updated, level, info with { CheckedAt = now });

            if (known is not null
                && !string.Equals(info.CurrentKeyVersion, known.CurrentKeyVersion, StringComparison.Ordinal)
                && updated.Rewrap is null or { CompletedAt: not null })
            {
                updated = updated with { Rewrap = new RewrapStatus { Protection = level, TargetKeyVersion = info.CurrentKeyVersion, StartedAt = now, Remaining = -1 } };
            }
        }

        if (updated.Byok is { } byok && _providers.TryGetValue(KeyValueProtection.Byok, out var customer))
        {
            try
            {
                var info = await customer.GetKeyInfoAsync(scope, updated, cancellationToken);
                var versionChanged = byok.CurrentKeyVersion is not null && !string.Equals(info.CurrentKeyVersion, byok.CurrentKeyVersion, StringComparison.Ordinal);
                updated = updated with
                {
                    Byok = byok with { Health = CustomerKeyHealth.Healthy, CurrentKeyVersion = info.CurrentKeyVersion, LastCheckedAt = now, LastError = null },
                };

                if (versionChanged && updated.Rewrap is null or { CompletedAt: not null })
                {
                    updated = updated with { Rewrap = new RewrapStatus { Protection = KeyValueProtection.Byok, TargetKeyVersion = info.CurrentKeyVersion, StartedAt = now, Remaining = -1 } };
                }
            }
            catch (StorytellerException exception)
            {
                updated = updated with { Byok = byok with { Health = CustomerKeyHealth.Unavailable, LastCheckedAt = now, LastError = exception.Message } };

                if (byok.Health != CustomerKeyHealth.Unavailable)
                {
                    await AuditAsync(organization, project, KeyValueAuditOperation.ByokUnavailable, exception.Message, cancellationToken);
                }
            }
        }

        if (updated == policy)
        {
            return policy;
        }

        return await SaveAsync(organization, project, updated, cancellationToken);
    }

    public async Task<RewrapStatus?> RewrapAsync(string organization, string project, KeyValueProtection protection, CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(protection, out var provider))
        {
            return null;
        }

        var policy = await _policies.GetAsync(organization, project, cancellationToken);
        var scope = new KeyValueScope(organization, project);
        var info = await provider.GetKeyInfoAsync(scope, policy, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var status = policy.Rewrap is { CompletedAt: null } pending && pending.Protection == protection
            ? pending with { TargetKeyVersion = info.CurrentKeyVersion }
            : new RewrapStatus { Protection = protection, TargetKeyVersion = info.CurrentKeyVersion, StartedAt = now, Remaining = -1 };

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var query = new QueryDefinition(
                "SELECT c.id, c.PartitionKey, c._etag, c.ttl, c._ts, c.Content FROM c "
                + "WHERE STARTSWITH(c.PartitionKey, @prefix) AND c.Content.Protection = @level AND IS_DEFINED(c.Content.Encrypted) "
                + "AND NOT ENDSWITH(c.Content.Encrypted.KeyId, @versionSuffix)")
            .WithParameter("@prefix", $"{project}.{PartitionKeys.KeyValuesPartitionInfix}.")
            .WithParameter("@level", protection.ToString())
            .WithParameter("@versionSuffix", $"/{info.CurrentKeyVersion}");

        long processed = 0;
        string? lastError = null;
        using var throttler = new SemaphoreSlim(Math.Max(1, _options.Protection.MaxConcurrentKeyOperations));
        using var iterator = container.GetItemQueryIterator<RewrapRow>(query, requestOptions: new QueryRequestOptions { MaxItemCount = 100 });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);

            await Task.WhenAll(page.Select(async row =>
            {
                await throttler.WaitAsync(cancellationToken);

                try
                {
                    if (await RewrapOneAsync(container, provider, scope, policy, row, cancellationToken))
                    {
                        Interlocked.Increment(ref processed);
                    }
                }
                catch (Exception exception) when (exception is StorytellerException or KeyValueIntegrityException or CosmosException)
                {
                    lastError = exception.Message;
                    _logger.LogWarning(exception, "Could not re-wrap {Id} of {Project}", row.Id, project);
                }
                finally
                {
                    throttler.Release();
                }
            }));
        }

        status = status with
        {
            Processed = status.Processed + processed,
            Remaining = lastError is null ? 0 : -1,
            CompletedAt = lastError is null ? _timeProvider.GetUtcNow() : null,
            LastError = lastError,
        };

        var latest = await _policies.GetAsync(organization, project, cancellationToken);
        await SaveAsync(organization, project, latest with { Rewrap = status }, cancellationToken);
        await AuditAsync(organization, project, KeyValueAuditOperation.Rewrap, $"{protection} -> {info.CurrentKeyVersion}: {processed} envelope(s){(lastError is null ? string.Empty : $", last error: {lastError}")}", cancellationToken);
        return status;
    }

    private static int? GetRemainingLifetime(RewrapRow row, DateTimeOffset now)
    {
        if (row.TimeToLive is null)
        {
            return null;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(row.Timestamp).AddSeconds(row.TimeToLive.Value);
        return (int)Math.Max(1, (expires - now).TotalSeconds);
    }

    private async Task<bool> RewrapOneAsync(Container container, IKeyEncryptionProvider provider, KeyValueScope scope, KeyValuePolicy policy, RewrapRow row, CancellationToken cancellationToken)
    {
        var encrypted = row.Content.Encrypted!;
        var dataKey = await provider.UnwrapAsync(scope, policy, encrypted, cancellationToken);

        try
        {
            var wrapped = await provider.WrapAsync(scope, policy, dataKey, cancellationToken);
            var updated = encrypted with
            {
                KeyId = wrapped.KeyId,
                Algorithm = wrapped.Algorithm,
                WrappedKey = Base64Url.EncodeToString(wrapped.Value),
            };

            var operations = new List<PatchOperation> { PatchOperation.Set("/Content/Encrypted", updated) };
            var remaining = GetRemainingLifetime(row, _timeProvider.GetUtcNow());

            if (remaining is not null)
            {
                // a patch resets _ts, which the ttl counts from
                operations.Add(PatchOperation.Set("/ttl", remaining.Value));
            }

            try
            {
                await container.PatchItemAsync<object>(
                    row.Id,
                    new PartitionKey(row.PartitionKey),
                    operations,
                    new PatchItemRequestOptions { IfMatchEtag = row.ETag },
                    cancellationToken);
                return true;
            }
            catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
            {
                // changed or expired meanwhile; the next run sees the new state
                return false;
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private async Task<KeyValuePolicy> SaveAsync(string organization, string project, KeyValuePolicy policy, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await _policies.SetAsync(organization, project, policy with
                {
                    Version = policy.Version + 1,
                    Author = "system",
                    UpdatedAt = _timeProvider.GetUtcNow(),
                }, cancellationToken);
            }
            catch (ConflictException exception) when (exception.ErrorCode == ErrorCodes.PolicyVersionMismatch && attempt < 2)
            {
                // a user changed the policy meanwhile: take their version and keep our key state
                var latest = await _policies.GetAsync(organization, project, cancellationToken);
                policy = latest with { Standard = policy.Standard, Hsm = policy.Hsm, Byok = policy.Byok, Rewrap = policy.Rewrap };
            }
        }

        throw new ConflictException($"The policy of '{organization}.{project}' keeps changing; the maintenance retries later.", ErrorCodes.PolicyVersionMismatch);
    }

    private async Task AuditAsync(string organization, string project, KeyValueAuditOperation operation, string? detail, CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        await _audit.WriteAsync(organization, project, new KeyValueAuditEvent
        {
            Id = CosmosKeyValueAuditLog.NewId(now),
            Time = now,
            Operation = operation,
            Actor = "system",
            Channel = KeyValueChannel.System,
            Detail = detail,
        }, cancellationToken);
    }

    private sealed record RewrapRow
    {
        [JsonProperty("id")]
        public required string Id { get; init; }

        public required string PartitionKey { get; init; }

        [JsonProperty("_etag")]
        public string? ETag { get; init; }

        [JsonProperty("ttl")]
        public int? TimeToLive { get; init; }

        [JsonProperty("_ts")]
        public long Timestamp { get; init; }

        public required StoredValue Content { get; init; }
    }
}
