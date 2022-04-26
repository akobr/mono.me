using System;
using System.Linq;

using SysPath = System.IO.Path;

namespace c0ded0c.Core
{
    public class ArtifactInfo : IArtifactInfo
    {
        private readonly string path;

        public ArtifactInfo(string key, string path)
        {
            Key = key;
            this.path = path;
            Content = Core.Content.Empty;
            Type = ContentType.Empty;
        }

        public ArtifactInfo(string key, string path, IContent content)
        {
            Key = key;
            this.path = path;
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Type = content.Type;
        }

        public string Key { get; }

        public string Path => path + GetExtension(Type);

        public ContentType Type { get; private set; }

        public IContent Content { get; private set; }

        public bool HasContent => Content != null && Type != ContentType.Empty;

        public void SetContent(IContent content)
        {
            lock (this)
            {
                Content = content ?? throw new ArgumentNullException(nameof(content));
                Type = content!.Type;
            }
        }

        public static ArtifactInfo BuildFromSubjectPath(string key, params ISubjectInfo[] subjects)
        {
            return new ArtifactInfo(key, BuildSubjectPath(subjects) + SysPath.DirectorySeparatorChar + key);
        }

        public static ArtifactInfo BuildFromSubjectPath(string key, IContent content, params ISubjectInfo[] subjects)
        {
            return new ArtifactInfo(key, BuildSubjectPath(subjects) + SysPath.DirectorySeparatorChar + key, content);
        }

        private static string BuildSubjectPath(ISubjectInfo[] subjects)
        {
            return string.Join(SysPath.DirectorySeparatorChar, subjects.Select(s => s.Key.Hash));
        }

        private static string GetExtension(ContentType type)
        {
            switch (type)
            {
                case ContentType.Text:
                    return ".txt";

                case ContentType.Json:
                    return ".json";

                case ContentType.Yaml:
                    return ".yaml";

                case ContentType.Xml:
                    return ".xml";

                case ContentType.PlantUml:
                    return ".puml";

                case ContentType.Markdown:
                    return ".md";

                case ContentType.Html:
                    return ".html";

                // case ContentType.Empty:
                default:
                    return string.Empty;
            }
        }
    }
}
