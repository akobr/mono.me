using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure.Security.KeyVault.Keys;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

public partial class CosmosKeyValuePolicyService : IKeyValuePolicyService
{
    private readonly IKeyValuePolicyStore _store;
    private readonly IReadOnlyDictionary<KeyValueProtection, IKeyEncryptionProvider> _providers;
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IKeyValueAuditLog? _audit;
    private readonly KeyValueOptions _options;
    private readonly TimeProvider _timeProvider;

    public CosmosKeyValuePolicyService(
        IKeyValuePolicyStore store,
        IEnumerable<IKeyEncryptionProvider> providers,
        IContainerRepositoryProvider repositoryProvider,
        IOptions<KeyValueOptions> options,
        IKeyValueAuditLog? audit = null,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _providers = providers.ToDictionary(provider => provider.Protection);
        _repositoryProvider = repositoryProvider;
        _audit = audit;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        return _store.GetAsync(organization, project, cancellationToken);
    }

    public async Task<KeyValuePolicy> UpdateAsync(string organization, string project, KeyValuePolicyUpdate update, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(caller);
        var current = await _store.GetAsync(organization, project, cancellationToken);
        EnsureExpectedVersion(current, update.ExpectedVersion);
        var policy = current;
        var changes = new List<string>();
        var scope = new KeyValueScope(organization, project);

        if (update.AllowedProtections is not null)
        {
            var allowed = update.AllowedProtections.Distinct().ToList();

            if (!allowed.Contains(KeyValueProtection.Public))
            {
                throw new InvalidInputException("Public pairs can not be disabled.", ErrorCodes.ProtectionNotAllowed);
            }

            foreach (var level in allowed.Except(current.AllowedProtections))
            {
                policy = await EnableAsync(policy, scope, level, cancellationToken);
                changes.Add($"+{level}");
            }

            foreach (var level in current.AllowedProtections.Except(allowed))
            {
                var inUse = await CountInUseAsync(organization, project, level, cancellationToken);

                if (inUse.Count > 0)
                {
                    throw KeyValueErrors.ProtectionInUse(level, inUse);
                }

                changes.Add($"-{level}");
            }

            policy = policy with { AllowedProtections = allowed };
        }

        if (update.DefaultSecretProtection is { } defaultSecret)
        {
            if (!defaultSecret.IsSecret() || !policy.AllowedProtections.Contains(defaultSecret))
            {
                throw new InvalidInputException($"The default secret level must be an allowed secret level; '{defaultSecret}' is not.", ErrorCodes.ProtectionNotAllowed);
            }

            policy = policy with { DefaultSecretProtection = defaultSecret };
            changes.Add($"default={defaultSecret}");
        }

        if (update.RequireMtlsForSecretDelivery is { } requireMtls && requireMtls != policy.RequireMtlsForSecretDelivery)
        {
            policy = policy with { RequireMtlsForSecretDelivery = requireMtls };
            changes.Add($"mtls={requireMtls}");
        }

        if (update.HsmRotationInterval is { } rotation)
        {
            if (rotation < TimeSpan.FromDays(1) || rotation > TimeSpan.FromDays(3650))
            {
                throw new InvalidInputException("The HSM rotation interval must be between 1 day and 10 years.", ErrorCodes.ProtectionNotAllowed);
            }

            policy = policy with { Hsm = (policy.Hsm ?? new HsmSettings()) with { RotationInterval = rotation } };
            changes.Add($"hsmRotation={rotation}");
        }

        if (update.Byok is { } byok)
        {
            policy = ApplyByok(policy, byok);
            changes.Add(string.IsNullOrWhiteSpace(byok.KeyUri) ? "byok=cleared" : $"byok={byok.KeyUri}");
        }

        if (changes.Count == 0)
        {
            return current;
        }

        var saved = await SaveAsync(organization, project, policy, caller, cancellationToken);
        await AuditAsync(organization, project, caller, KeyValueAuditOperation.PolicyChange, string.Join(", ", changes), cancellationToken);
        return saved;
    }

    public ByokSetupInfo GetByokSetup(string organization, string project)
    {
        return GetCustomerKeyProvider().GetSetupInfo();
    }

    public async Task<KeyValuePolicy> VerifyByokAsync(string organization, string project, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var provider = GetCustomerKeyProvider();
        var current = await _store.GetAsync(organization, project, cancellationToken);
        var settings = current.Byok ?? throw new ConflictException("The project has no customer key (BYOK) to verify.", ErrorCodes.ByokNotConfigured);
        var now = _timeProvider.GetUtcNow();
        var verification = await provider.VerifyAsync(settings, cancellationToken);

        var updated = settings with
        {
            VerifiedAt = verification.IsValid ? now : null,
            CurrentKeyVersion = verification.CurrentKeyVersion,
            Health = verification.IsValid ? CustomerKeyHealth.Healthy : CustomerKeyHealth.Unavailable,
            LastCheckedAt = now,
            LastError = verification.Error,
        };

        var saved = await SaveAsync(organization, project, current with { Byok = updated }, caller, cancellationToken);
        await AuditAsync(organization, project, caller, KeyValueAuditOperation.ByokVerify, verification.IsValid ? "verified" : verification.Error, cancellationToken);

        if (!verification.IsValid)
        {
            throw new FailedDependencyException($"The customer key could not be verified: {verification.Error}", ErrorCodes.CustomerKeyInvalid);
        }

        return saved;
    }

    public async Task<KeyValuePolicy> RotateAsync(string organization, string project, KeyValueProtection protection, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (protection is not (KeyValueProtection.Standard or KeyValueProtection.Hsm))
        {
            throw new InvalidInputException($"Only the Standard and Hsm keys are rotated by the platform; '{protection}' is not.", ErrorCodes.NotSupportedForProtection);
        }

        var provider = GetProvider(protection);
        var current = await _store.GetAsync(organization, project, cancellationToken);
        var info = await provider.RotateAsync(new KeyValueScope(organization, project), current, cancellationToken);
        var policy = WithKeyInfo(current, protection, info) with
        {
            Rewrap = new RewrapStatus
            {
                Protection = protection,
                TargetKeyVersion = info.CurrentKeyVersion,
                StartedAt = _timeProvider.GetUtcNow(),
                Remaining = -1,
            },
        };

        var saved = await SaveAsync(organization, project, policy, caller, cancellationToken);
        await AuditAsync(organization, project, caller, KeyValueAuditOperation.Rotate, $"{protection} -> {info.CurrentKeyVersion}", cancellationToken);
        return saved;
    }

    public async Task<ClientEncryptionRecipient> AddRecipientAsync(string organization, string project, ClientEncryptionRecipientCreate create, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(caller);
        var current = await _store.GetAsync(organization, project, cancellationToken);
        var id = string.IsNullOrWhiteSpace(create.Id) ? Guid.NewGuid().ToString("N")[..16] : create.Id.Trim();

        if (!RecipientIdPattern().IsMatch(id))
        {
            throw new InvalidInputException("A recipient id has 1 to 64 letters, digits, '_' or '-'.", ErrorCodes.RecipientInvalid);
        }

        if (string.IsNullOrWhiteSpace(create.Name) || create.Name.Trim().Length > 100)
        {
            throw new InvalidInputException("A recipient name has 1 to 100 characters.", ErrorCodes.RecipientInvalid);
        }

        if (current.Recipients.Any(recipient => string.Equals(recipient.Id, id, StringComparison.Ordinal)))
        {
            throw new ConflictException($"The recipient '{id}' already exists.", ErrorCodes.RecipientExists);
        }

        if (current.Recipients.Count >= _options.MaxRecipients)
        {
            throw new ConflictException($"A project has at most {_options.MaxRecipients} recipients.", ErrorCodes.RecipientInvalid);
        }

        RsaPublicJwk key;

        try
        {
            key = RsaPublicJwk.Parse(create.PublicKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidInputException($"The public key is not valid: {exception.Message}", ErrorCodes.RecipientInvalid);
        }

        if (key.ModulusBits < RsaPublicJwk.MinimumModulusBits)
        {
            throw new InvalidInputException($"The public key has {key.ModulusBits} bits; at least {RsaPublicJwk.MinimumModulusBits} are needed.", ErrorCodes.RecipientInvalid);
        }

        var recipient = new ClientEncryptionRecipient
        {
            Id = id,
            Name = create.Name.Trim(),
            PublicKey = new RsaJsonWebKey { N = key.N, E = key.E },
            IsEnabled = true,
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = caller.Author,
        };

        await SaveAsync(organization, project, current with { Recipients = [.. current.Recipients, recipient] }, caller, cancellationToken);
        await AuditAsync(organization, project, caller, KeyValueAuditOperation.RecipientAdded, id, cancellationToken);
        return recipient;
    }

    public async Task<ClientEncryptionRecipient> UpdateRecipientAsync(string organization, string project, string id, ClientEncryptionRecipientUpdate update, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(caller);
        var current = await _store.GetAsync(organization, project, cancellationToken);
        var existing = FindRecipient(current, id);
        var updated = existing with
        {
            Name = string.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name.Trim(),
            IsEnabled = update.IsEnabled ?? existing.IsEnabled,
        };

        var recipients = current.Recipients.Select(recipient => recipient.Id == id ? updated : recipient).ToList();
        EnsureHyokStillPossible(current, recipients);
        await SaveAsync(organization, project, current with { Recipients = recipients }, caller, cancellationToken);
        await AuditAsync(organization, project, caller, KeyValueAuditOperation.RecipientChanged, $"{id}: enabled={updated.IsEnabled}", cancellationToken);
        return updated;
    }

    public async Task RemoveRecipientAsync(string organization, string project, string id, KeyValueCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var current = await _store.GetAsync(organization, project, cancellationToken);
        FindRecipient(current, id);
        var recipients = current.Recipients.Where(recipient => recipient.Id != id).ToList();
        EnsureHyokStillPossible(current, recipients);
        await SaveAsync(organization, project, current with { Recipients = recipients }, caller, cancellationToken);
        await AuditAsync(organization, project, caller, KeyValueAuditOperation.RecipientRemoved, id, cancellationToken);
    }

    // Keys of the project, active or deleted, per view, that still carry the level.
    public async Task<IReadOnlyDictionary<string, int>> CountInUseAsync(string organization, string project, KeyValueProtection protection, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT c.ViewName AS View, COUNT(1) AS Count FROM c "
                + "WHERE STARTSWITH(c.PartitionKey, @prefix) AND STARTSWITH(c.id, @idPrefix) AND c.Content.Protection = @level GROUP BY c.ViewName")
            .WithParameter("@prefix", $"{project}.{PartitionKeys.KeyValuesPartitionInfix}.")
            .WithParameter("@idPrefix", $"{EntityIdPrefixTypes.KeyValue}.")
            .WithParameter("@level", protection.ToString());

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        using var iterator = container.GetItemQueryIterator<ViewCountRow>(query);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        while (iterator.HasMoreResults)
        {
            foreach (var row in await iterator.ReadNextAsync(cancellationToken))
            {
                counts[row.View ?? string.Empty] = row.Count;
            }
        }

        return counts;
    }

    internal static KeyValuePolicy WithKeyInfo(KeyValuePolicy policy, KeyValueProtection protection, ProtectionKeyInfo info)
    {
        return protection switch
        {
            KeyValueProtection.Standard => policy with { Standard = info },
            KeyValueProtection.Hsm => policy with { Hsm = (policy.Hsm ?? new HsmSettings()) with { Key = info } },
            _ => policy,
        };
    }

    private static void EnsureExpectedVersion(KeyValuePolicy current, ulong? expectedVersion)
    {
        if (expectedVersion is not null && expectedVersion != current.Version)
        {
            throw new ConflictException(
                $"The policy is at version {current.Version}, not {expectedVersion}; reload and retry.",
                ErrorCodes.PolicyVersionMismatch);
        }
    }

    private static ClientEncryptionRecipient FindRecipient(KeyValuePolicy policy, string id)
    {
        return policy.Recipients.FirstOrDefault(recipient => string.Equals(recipient.Id, id, StringComparison.Ordinal))
            ?? throw new NotFoundException($"The recipient '{id}' does not exist.", ErrorCodes.RecipientNotFound);
    }

    private static void EnsureHyokStillPossible(KeyValuePolicy policy, IReadOnlyList<ClientEncryptionRecipient> recipients)
    {
        if (policy.AllowedProtections.Contains(KeyValueProtection.Hyok) && !recipients.Any(recipient => recipient.IsEnabled))
        {
            throw new ConflictException("Client-side encryption (Hyok) is allowed and needs at least one enabled recipient; disable the level first.", ErrorCodes.ProtectionInUse);
        }
    }

    private static KeyValuePolicy ApplyByok(KeyValuePolicy policy, ByokSettingsUpdate byok)
    {
        var allowed = policy.AllowedProtections.Contains(KeyValueProtection.Byok);

        if (string.IsNullOrWhiteSpace(byok.KeyUri))
        {
            if (allowed)
            {
                throw new ConflictException("The customer key is in use; disable the Byok level before removing it.", ErrorCodes.ProtectionInUse);
            }

            return policy with { Byok = null };
        }

        if (!Uri.TryCreate(byok.KeyUri.Trim(), UriKind.Absolute, out var keyUri) || !string.Equals(keyUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidInputException("The customer key id must be an https key URI, e.g. https://vault.vault.azure.net/keys/name.", ErrorCodes.CustomerKeyInvalid);
        }

        KeyVaultKeyIdentifier identifier;

        try
        {
            identifier = new KeyVaultKeyIdentifier(keyUri);
        }
        catch (ArgumentException)
        {
            throw new InvalidInputException("The customer key id must be a Key Vault key URI, e.g. https://vault.vault.azure.net/keys/name.", ErrorCodes.CustomerKeyInvalid);
        }

        if (!Guid.TryParse(byok.TenantId, out var tenant))
        {
            throw new InvalidInputException("The customer tenant id must be a GUID.", ErrorCodes.CustomerKeyInvalid);
        }

        var versionless = new Uri(identifier.VaultUri, $"keys/{identifier.Name}").ToString();

        if (allowed && policy.Byok is not null
            && (!string.Equals(policy.Byok.KeyUri, versionless, StringComparison.OrdinalIgnoreCase) || !string.Equals(policy.Byok.TenantId, tenant.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("The customer key is in use; disable the Byok level before changing it.", ErrorCodes.ProtectionInUse);
        }

        if (policy.Byok is not null
            && string.Equals(policy.Byok.KeyUri, versionless, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.Byok.TenantId, tenant.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return policy;
        }

        return policy with
        {
            Byok = new ByokSettings
            {
                KeyUri = versionless,
                TenantId = tenant.ToString(),
                Health = CustomerKeyHealth.Unknown,
            },
        };
    }

    private IKeyEncryptionProvider GetProvider(KeyValueProtection protection)
    {
        return _providers.TryGetValue(protection, out var provider) ? provider : throw KeyValueErrors.ProtectionNotConfigured(protection);
    }

    private ICustomerKeyProvider GetCustomerKeyProvider()
    {
        return GetProvider(KeyValueProtection.Byok) as ICustomerKeyProvider ?? throw KeyValueErrors.ProtectionNotConfigured(KeyValueProtection.Byok);
    }

    private async Task<KeyValuePolicy> EnableAsync(KeyValuePolicy policy, KeyValueScope scope, KeyValueProtection level, CancellationToken cancellationToken)
    {
        switch (level)
        {
            case KeyValueProtection.Standard:
            case KeyValueProtection.Hsm:
                var provider = GetProvider(level);
                var info = await provider.EnsureKeyAsync(scope, policy, cancellationToken);
                policy = WithKeyInfo(policy, level, info);

                if (level == KeyValueProtection.Hsm && provider is IHsmBackendProvider backendAware)
                {
                    policy = policy with { Hsm = policy.Hsm! with { Backend = backendAware.GetBackend(scope) } };
                }

                return policy;

            case KeyValueProtection.Byok:
                if (policy.Byok?.VerifiedAt is null)
                {
                    throw new ConflictException("Set and verify the customer key before enabling the Byok level.", ErrorCodes.ByokNotConfigured);
                }

                GetCustomerKeyProvider();
                return policy;

            case KeyValueProtection.Hyok:
                if (!policy.Recipients.Any(recipient => recipient.IsEnabled))
                {
                    throw new ConflictException("Add at least one enabled recipient before enabling client-side encryption (Hyok).", ErrorCodes.RecipientInvalid);
                }

                return policy;

            default:
                return policy;
        }
    }

    private async Task<KeyValuePolicy> SaveAsync(string organization, string project, KeyValuePolicy policy, KeyValueCaller caller, CancellationToken cancellationToken)
    {
        return await _store.SetAsync(organization, project, policy with
        {
            Version = policy.Version + 1,
            Author = caller.Author,
            UpdatedAt = _timeProvider.GetUtcNow(),
        }, cancellationToken);
    }

    private async Task AuditAsync(string organization, string project, KeyValueCaller caller, KeyValueAuditOperation operation, string? detail, CancellationToken cancellationToken)
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
            Actor = caller.Author,
            Channel = caller.Channel,
            CorrelationId = caller.CorrelationId,
            Detail = detail,
        }, cancellationToken);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex RecipientIdPattern();

    private sealed record ViewCountRow(string? View, int Count);
}
