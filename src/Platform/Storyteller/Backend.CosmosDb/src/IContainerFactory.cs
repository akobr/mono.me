using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public interface IContainerFactory
{
    Task<Container> CreateContainerIfNotExistsAsync(string containerName);

    // TTL without a default expiry and the audit entries left out of the index.
    Task EnsureContainerSettingsAsync(Container container);

    // Applies the settings to every organization container of the database; returns their names.
    Task<IReadOnlyList<string>> EnsureOrganizationContainersAsync();

    // Names of the containers of the platform database.
    Task<IReadOnlyList<string>> GetContainerNamesAsync();
}
