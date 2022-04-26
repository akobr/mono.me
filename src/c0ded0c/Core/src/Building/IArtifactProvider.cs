using System.Collections.Generic;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IArtifactProvider
    {
        Task<IReadOnlyCollection<string>> GetKeysAsync(ISubjectInfo subject);

        Task<IReadOnlyCollection<IArtifactInfo>> GetAllAsync(ISubjectInfo subject);

        Task<IArtifactInfo> GetAsync(string key, ISubjectInfo subject);
    }
}
