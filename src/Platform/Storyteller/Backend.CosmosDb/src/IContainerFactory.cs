using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public interface IContainerFactory
{
    Task<Container> CreateContainerIfNotExistsAsync(string containerName);
}
