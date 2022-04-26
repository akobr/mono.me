using System;

namespace c0ded0c.Core
{
    public class ArtifactBuilder : IArtifactBuilder
    {
        private readonly IArtifactContentBuilder contentBuilder;

        public ArtifactBuilder(
            IArtifactContentBuilder contentBuilder)
        {
            this.contentBuilder = contentBuilder ?? throw new ArgumentNullException(nameof(contentBuilder));
        }

        public IArtifactInfo Build<TObject>(TObject value, string key, ISubjectInfo target)
            where TObject : class
        {
            key = key.ToLowerInvariant();

            return new ArtifactInfo(
                key,
                GenerateArtifactPath(key, target),
                contentBuilder.Build(value));
        }

        public IArtifactInfo BuildSpecific(IContent value, string key, ISubjectInfo target)
        {
            key = key.ToLowerInvariant();

            return new ArtifactInfo(
                key,
                GenerateArtifactPath(key, target),
                value);
        }

        private string GenerateArtifactPath(string key, ISubjectInfo target)
        {
            return target.Key.Path + "/" + key.ToLowerInvariant();
        }
    }
}
