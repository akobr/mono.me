using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class ArtifactProvider : IArtifactProvider
    {
        private readonly IStoringEngine storer;

        public ArtifactProvider(IStoringEngine storer)
        {
            this.storer = storer ?? throw new ArgumentNullException(nameof(storer));
        }

        public Task<IReadOnlyCollection<IArtifactInfo>> GetAllAsync(ISubjectInfo subject)
        {
            throw new NotImplementedException();
        }

        public Task<IArtifactInfo> GetAsync(string key, ISubjectInfo subject)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyCollection<string>> GetKeysAsync(ISubjectInfo subject)
        {
            throw new NotImplementedException();
        }
    }
}
