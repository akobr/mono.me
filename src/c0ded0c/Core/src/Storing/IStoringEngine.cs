using System;
using System.IO;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IStoringEngine : IEngine
    {
        Task StoreArtifactAsync(IArtifactInfo artifact);

        Task LoadArtifactAsync(IArtifactInfo artifact);

        Task<TObject?> LoadArtifactAsync<TObject>(IArtifactInfo artifact)
            where TObject : class;

        void RegisterRetrievingStrategy(ContentType type, Func<Stream, Task<IContent>> strategy);
    }
}
