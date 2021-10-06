using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _42.Monorepo.Cli.Cache
{
    public class FileContentCache : IFileContentCache
    {
        private readonly ConcurrentDictionary<string, FileContent> cache;

        public FileContentCache()
        {
            cache = new ConcurrentDictionary<string, FileContent>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsLoaded(string filePath)
        {
            return cache.TryGetValue(filePath, out var content)
                   && content.IsLoaded;
        }

        public Task<XDocument> GetOrLoadXmlContentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return cache
                .GetOrAdd(filePath, PrepareXmlContent)
                .Xml.GetValueAsync(cancellationToken);
        }

        public Task<JsonDocument> GetOrLoadJsonContentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return cache
                .GetOrAdd(filePath, PrepareJsonContent)
                .Json.GetValueAsync(cancellationToken);
        }

        private static FileContent PrepareXmlContent(string path)
        {
            return new FileContent(path, new AsyncLazy<XDocument>(t => XmlFactory(path, t)));
        }

        private static FileContent PrepareJsonContent(string path)
        {
            return new FileContent(path, new AsyncLazy<JsonDocument>(t => JsonFactory(path, t)));
        }

        private static async Task<XDocument> XmlFactory(string path, CancellationToken cancellationToken)
        {
            using var reader = File.OpenText(path);
            return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        }

        private static async Task<JsonDocument> JsonFactory(string path, CancellationToken cancellationToken)
        {
            await using var reader = File.OpenRead(path);
            return await JsonDocument.ParseAsync(
                reader,
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = 256,
                },
                cancellationToken);
        }

        private class FileContent
        {
            private readonly string path;
            private readonly ContentType type;
            private readonly AsyncLazy<XDocument>? xmlContent;
            private readonly AsyncLazy<JsonDocument>? jsonContent;

            public FileContent(string filePath, AsyncLazy<XDocument> xml)
            {
                path = filePath;
                type = ContentType.Xml;
                xmlContent = xml;
                jsonContent = null;
            }

            public FileContent(string filePath, AsyncLazy<JsonDocument> json)
            {
                path = filePath;
                type = ContentType.Json;
                jsonContent = json;
                xmlContent = null;
            }

            private enum ContentType : byte
            {
                Xml,
                Json,
            }

            public AsyncLazy<XDocument> Xml
                => xmlContent ?? throw new InvalidOperationException($"The file '{path}' is not Xml document.");

            public AsyncLazy<JsonDocument> Json
                => jsonContent ?? throw new InvalidOperationException($"The file '{path}' is not Json document.");

            public bool IsLoaded
            {
                get
                {
                    return type switch
                    {
                        ContentType.Xml => Xml.Task.IsCompleted,
                        _ => Json.Task.IsCompleted,
                    };
                }
            }
        }
    }
}
