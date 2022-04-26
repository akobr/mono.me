using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public static class ArtifactExtensions
    {
        public static Task<IArtifactInfo> AsStoreModels<TInfo>(this IEnumerable<TInfo> infos, string key, ISubjectInfo target, IArtifactManager manager)
            where TInfo : ISubjectInfo
        {
            return manager.CreateAsync(infos.Select((s) => new SubjectStoreModel(s)), key, target);
        }

        public static string BuildPath(params string[] segments)
        {
            return string.Join('/', segments.Where((s) => !string.IsNullOrWhiteSpace(s)));
        }
    }
}
