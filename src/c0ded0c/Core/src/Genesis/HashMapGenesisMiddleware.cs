using System;
using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public class HashMapGenesisMiddleware : IGenesisMiddleware
    {
        private readonly IArtifactManager artifactManager;
        private readonly IIdentificationMap identificationMap;

        public HashMapGenesisMiddleware(
            IArtifactManager artifactManager,
            IIdentificationMap identificationMap)
        {
            this.artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
            this.identificationMap = identificationMap ?? throw new ArgumentNullException(nameof(identificationMap));
        }

        public async Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next)
        {
            await artifactManager.CreateAsync(identificationMap.GetMap(), ArtifactKeys.HASH_MAP, workspace);
            return await next(workspace);
        }
    }
}
