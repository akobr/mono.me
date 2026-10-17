using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// The one signing key of Storyteller tokens in the core container. Concurrent bootstraps resolve through
// the create conflict: the losers read the winner's key, as for the machine certificate authority.
public class CosmosTokenSigningKeyStore : ITokenSigningKeyStore
{
    private const string Partition = "signing-keys";
    private const string KeyId = "tsk.1";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosTokenSigningKeyStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task<TokenSigningKey?> GetAsync(CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetCore();
        var entity = await repository.Container.TryReadItemAsync(
            KeyId,
            new PartitionKey(Partition),
            stream => stream.DeserializeSystemTextJson<TokenSigningKeyEntity>(_serializerOptions),
            cancellationToken: cancellationToken);

        return entity is null
            ? null
            : new TokenSigningKey(entity.Kid, entity.Algorithm, entity.PrivateKeyPkcs8, entity.CreatedAt);
    }

    public async Task<bool> TryCreateAsync(TokenSigningKey key, CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetCore();
        var entity = new TokenSigningKeyEntity
        {
            Id = KeyId,
            Kid = key.Kid,
            Algorithm = key.Algorithm,
            PrivateKeyPkcs8 = key.PrivateKeyPkcs8,
            CreatedAt = key.CreatedAt,
        };

        try
        {
            await repository.Container.CreateItemAsync(
                entity,
                new PartitionKey(Partition),
                new ItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken);
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }
}
