using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public interface IContainerFactory
{
    // The container organizations with its hierarchical key and settings; any other container (core) with /PartitionKey.
    Task<Container> CreateContainerIfNotExistsAsync(string containerName);

    // TTL without a default expiry and the audit entries left out of the index.
    Task EnsureContainerSettingsAsync(Container container);

    // The shared container of every organization's data: created when missing, its settings applied.
    Task<Container> EnsureOrganizationsContainerAsync();

    // Why the database can't take the shared layout: containers of the layout before it (org.{organization}), or a
    // container organizations with other partition key paths. Empty when the database is missing or fine. Reads only.
    Task<IReadOnlyList<string>> FindLayoutProblemsAsync();

    // Names of the containers of the platform database.
    Task<IReadOnlyList<string>> GetContainerNamesAsync();
}
