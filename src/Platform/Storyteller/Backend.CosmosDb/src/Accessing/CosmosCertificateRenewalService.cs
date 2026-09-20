using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CosmosCertificateRenewalService : ICertificateRenewalService
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IClientCertificateAuthority _authority;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<CosmosCertificateRenewalService> _logger;

    public CosmosCertificateRenewalService(
        IContainerRepositoryProvider repositoryProvider,
        IClientCertificateAuthority authority,
        IOptions<MachineAuthenticationOptions> options,
        IOptions<JsonSerializerOptions> serializerOptions,
        ILogger<CosmosCertificateRenewalService> logger)
    {
        _repositoryProvider = repositoryProvider;
        _authority = authority;
        _options = options;
        _serializerOptions = serializerOptions.Value;
        _logger = logger;
    }

    public async Task<CertificateRenewalResult> RenewAsync(
        string organization,
        string project,
        string machineAccessId,
        string presentingThumbprint)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = new PartitionKey(partitionKeyValue);

        var entity = await repository.Container.TryReadItemAsync(
            machineAccessId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (entity is null)
        {
            return new CertificateRenewalResult
            {
                Outcome = CertificateRenewalOutcome.NotFound,
                Message = $"Machine access '{machineAccessId}' not found.",
            };
        }

        return await RenewCoreAsync(repository, partitionKey, entity, organization, project, machineAccessId, presentingThumbprint);
    }

    private async Task<CertificateRenewalResult> RenewCoreAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        MachineAccessEntity entity,
        string organization,
        string project,
        string machineAccessId,
        string presentingThumbprint)
    {
        var opts = _options.Value;

        // Check if the presenting cert already matches PreviousThumbprint (already renewed).
        if (entity.PreviousThumbprint is not null
            && string.Equals(entity.PreviousThumbprint, presentingThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return new CertificateRenewalResult
            {
                Outcome = CertificateRenewalOutcome.AlreadyRenewed,
                LastRenewalAt = entity.LastRenewalAt,
                Message = $"The certificate for this machine access was already renewed at {entity.LastRenewalAt:O}. "
                    + "The new certificate was delivered to the first renewing instance. "
                    + "Propagation of the renewed certificate to other instances sharing this machine access "
                    + "is the responsibility of your deployment infrastructure (e.g., shared volume, secret store, orchestrator).",
            };
        }

        // Presenting cert must match the current thumbprint.
        if (entity.CertificateThumbprint is null
            || !string.Equals(entity.CertificateThumbprint, presentingThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return new CertificateRenewalResult
            {
                Outcome = CertificateRenewalOutcome.NotFound,
                Message = "The presenting certificate does not match the current certificate for this machine access.",
            };
        }

        // Check renewal window.
        if (entity.NotAfter is null)
        {
            return new CertificateRenewalResult
            {
                Outcome = CertificateRenewalOutcome.NotFound,
                Message = "Machine access has no certificate expiry information.",
            };
        }

        var renewalStart = entity.NotAfter.Value.AddDays(-opts.RenewalWindowDays);

        if (DateTimeOffset.UtcNow < renewalStart)
        {
            return new CertificateRenewalResult
            {
                Outcome = CertificateRenewalOutcome.NotInRenewalWindow,
                Message = $"Renewal is not yet permitted. The renewal window opens at {renewalStart:O} "
                    + $"({opts.RenewalWindowDays} days before expiry at {entity.NotAfter.Value:O}).",
            };
        }

        // Issue new certificate.
        var lifetimeDays = Math.Clamp(opts.CertificateLifetimeDays, opts.MinCertificateLifetimeDays, opts.MaxCertificateLifetimeDays);
        var issued = await _authority.IssueCertificateAsync(
            organization,
            project,
            machineAccessId,
            entity.Scope,
            lifetimeDays);

        // Compute PreviousValidUntil: min(old NotAfter, now + RenewalOverlapDays).
        var overlapExpiry = DateTimeOffset.UtcNow.AddDays(opts.RenewalOverlapDays);
        var previousValidUntil = entity.NotAfter.Value < overlapExpiry ? entity.NotAfter.Value : overlapExpiry;

        // CAS write.
        var updated = entity with
        {
            CertificateThumbprint = issued.Thumbprint,
            PreviousThumbprint = entity.CertificateThumbprint,
            PreviousValidUntil = previousValidUntil,
            CertificateSerialNumber = issued.SerialNumber,
            NotBefore = issued.NotBefore,
            NotAfter = issued.NotAfter,
            LastRenewalAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await repository.Container.ReplaceItemAsync(
                updated,
                updated.Id,
                partitionKey,
                new ItemRequestOptions { IfMatchEtag = entity.ETag });

            _logger.LogInformation(
                "Certificate renewed for machine {MachineAccessId}: {OldThumbprint} -> {NewThumbprint}",
                entity.Id,
                entity.CertificateThumbprint,
                issued.Thumbprint);

            return new CertificateRenewalResult
            {
                Outcome = CertificateRenewalOutcome.Success,
                Pkcs12 = issued.Pkcs12,
                Password = issued.Password,
                Thumbprint = issued.Thumbprint,
                LastRenewalAt = updated.LastRenewalAt,
            };
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // Another instance won the race. Re-read and check.
            _logger.LogInformation("Certificate renewal CAS conflict for {MachineAccessId} — re-reading", entity.Id);

            var reread = await repository.Container.TryReadItemAsync(
                entity.Id,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

            if (reread?.PreviousThumbprint is not null
                && string.Equals(reread.PreviousThumbprint, presentingThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return new CertificateRenewalResult
                {
                    Outcome = CertificateRenewalOutcome.AlreadyRenewed,
                    LastRenewalAt = reread.LastRenewalAt,
                    Message = $"The certificate for this machine access was already renewed at {reread.LastRenewalAt:O}. "
                        + "The new certificate was delivered to the first renewing instance. "
                        + "Propagation of the renewed certificate to other instances sharing this machine access "
                        + "is the responsibility of your deployment infrastructure (e.g., shared volume, secret store, orchestrator).",
                };
            }

            throw;
        }
    }
}
