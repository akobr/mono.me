using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller
{
    public interface ICosmosClientProvider
    {
        CosmosClient Client { get; }
    }
}
