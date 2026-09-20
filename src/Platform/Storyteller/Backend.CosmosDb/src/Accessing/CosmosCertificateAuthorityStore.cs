using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CosmosCertificateAuthorityStore : ICertificateAuthorityStore
{
    private const string Partition = "certificates";
    private const string ActiveIdPrefix = "cau.";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosCertificateAuthorityStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task<byte[]?> GetActivePkcs12Async()
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(Partition);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.IsActive = true ORDER BY c.CreatedAt DESC");
        using var iterator = repository.Container.GetItemQueryIterator<CertificateAuthorityEntity>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey, MaxItemCount = 1 });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            var entity = response.FirstOrDefault();
            return entity?.Pkcs12Data;
        }

        return null;
    }

    public async Task StorePkcs12Async(byte[] pkcs12, string version)
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(Partition);

        using var cert = X509CertificateLoader.LoadPkcs12(pkcs12, null);

        var entity = new CertificateAuthorityEntity
        {
            Id = $"{ActiveIdPrefix}{version}",
            CertificateData = cert.RawData,
            Pkcs12Data = pkcs12,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            IsActive = true,
        };

        try
        {
            await repository.Container.CreateItemAsync(entity, partitionKey);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Another instance already stored the CA — that's fine.
        }
    }

    public async Task<CertificateAuthorityRecord?> GetActiveRecordAsync()
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(Partition);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.IsActive = true ORDER BY c.CreatedAt DESC");
        using var iterator = repository.Container.GetItemQueryIterator<CertificateAuthorityEntity>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey, MaxItemCount = 1 });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            var entity = response.FirstOrDefault();

            if (entity is not null)
            {
                return new CertificateAuthorityRecord(
                    entity.CertificateData,
                    entity.Pkcs12Data,
                    entity.KeyVaultKeyIdentifier,
                    entity.Version);
            }
        }

        return null;
    }

    public async Task StoreCertificateAsync(byte[] certificateData, string version, string? keyVaultKeyIdentifier)
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(Partition);

        using var cert = new X509Certificate2(certificateData);

        var entity = new CertificateAuthorityEntity
        {
            Id = $"{ActiveIdPrefix}{version}",
            CertificateData = certificateData,
            Pkcs12Data = null,
            KeyVaultKeyIdentifier = keyVaultKeyIdentifier,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero),
            IsActive = true,
        };

        try
        {
            await repository.Container.CreateItemAsync(entity, partitionKey);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Another instance already stored the CA — that's fine.
        }
    }

    public async Task<IReadOnlyList<X509Certificate2>> GetAllCertificatesAsync()
    {
        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(Partition);

        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.CreatedAt DESC");
        using var iterator = repository.Container.GetItemQueryIterator<CertificateAuthorityEntity>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        var certificates = new List<X509Certificate2>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var entity in response)
            {
                var cert = new X509Certificate2(entity.CertificateData);
                if (cert.NotAfter > DateTime.UtcNow)
                {
                    certificates.Add(cert);
                }
            }
        }

        return certificates;
    }
}
