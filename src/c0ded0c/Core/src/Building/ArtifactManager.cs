using System;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class ArtifactManager : IArtifactManager
    {
        private readonly IStoringEngine storer;

        public ArtifactManager(
            IArtifactProvider provider,
            IArtifactBuilder builder,
            IStoringEngine storer)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Builder = builder ?? throw new ArgumentNullException(nameof(builder));
            this.storer = storer ?? throw new ArgumentNullException(nameof(storer));
        }

        public IArtifactProvider Provider { get; }

        public IArtifactBuilder Builder { get; }

        public async Task<IArtifactInfo> CreateAsync<TObject>(TObject value, string key, ISubjectInfo target)
            where TObject : class
        {
            IArtifactInfo artifact = Builder.Build(value, key, target);
            await storer.StoreArtifactAsync(artifact);
            return artifact;
        }

        public async Task<IArtifactInfo> CreateSpecificAsync(IContent value, string key, ISubjectInfo target)
        {
            IArtifactInfo artifact = Builder.BuildSpecific(value, key, target);
            await storer.StoreArtifactAsync(artifact);
            return artifact;
        }
    }
}
