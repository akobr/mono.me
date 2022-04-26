using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IArtifactManager
    {
        public IArtifactProvider Provider { get; }

        public IArtifactBuilder Builder { get; }

        public Task<IArtifactInfo> CreateAsync<TObject>(TObject value, string key, ISubjectInfo target)
            where TObject : class;

        public Task<IArtifactInfo> CreateSpecificAsync(IContent value, string key, ISubjectInfo target);
    }
}
