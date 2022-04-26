using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public class SearchIndexGenesisMiddleware : IGenesisMiddleware
    {
        private readonly IArtifactManager artifactManager;

        public SearchIndexGenesisMiddleware(IArtifactManager artifactManager)
        {
            this.artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        }

        public async Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next)
        {
            Dictionary<string, HashSet<IIdentificator>> index = new Dictionary<string, HashSet<IIdentificator>>();

            // TODO: [P1] process all docs, namespaces, types, members and generate map with words and characters (first letters)
            await artifactManager.CreateAsync(index, ArtifactKeys.SEARCH_INDEX, workspace);
            return await next(workspace);
        }
    }
}
