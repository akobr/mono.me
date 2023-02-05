using System;
using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _42.Monorepo.Cli.Cache
{
    public class FileContentCache : IFileContentCache
    {
        private readonly IFileSystem _fileSystem;
        private readonly ConcurrentDictionary<string, FileContent> _cache;

        public FileContentCache(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
            _cache = new ConcurrentDictionary<string, FileContent>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsLoaded(string filePath)
        {
            return _cache.TryGetValue(filePath, out var content)
                   && content.IsLoaded;
        }

        public Task<XDocument> GetOrLoadXmlContentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return _cache
                .GetOrAdd(filePath, PrepareXmlContent)
                .Xml.GetValueAsync(cancellationToken);
        }

        public Task<JsonDocument> GetOrLoadJsonContentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return _cache
                .GetOrAdd(filePath, PrepareJsonContent)
                .Json.GetValueAsync(cancellationToken);
        }

        private FileContent PrepareXmlContent(string path)
        {
            return new FileContent(path, new AsyncLazy<XDocument>(t => XmlFactory(path, t)));
        }

        private FileContent PrepareJsonContent(string path)
        {
            return new FileContent(path, new AsyncLazy<JsonDocument>(t => JsonFactory(path, t)));
        }

        private async Task<XDocument> XmlFactory(string path, CancellationToken cancellationToken)
        {
            using var reader = _fileSystem.File.OpenText(path);
            return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        }

        private async Task<JsonDocument> JsonFactory(string path, CancellationToken cancellationToken)
        {
            await using var reader = _fileSystem.File.OpenRead(path);
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
            private readonly string _path;
            private readonly ContentType _type;
            private readonly AsyncLazy<XDocument>? _xmlContent;
            private readonly AsyncLazy<JsonDocument>? _jsonContent;

            public FileContent(string filePath, AsyncLazy<XDocument> xml)
            {
                _path = filePath;
                _type = ContentType.Xml;
                _xmlContent = xml;
                _jsonContent = null;
            }

            public FileContent(string filePath, AsyncLazy<JsonDocument> json)
            {
                _path = filePath;
                _type = ContentType.Json;
                _jsonContent = json;
                _xmlContent = null;
            }

            private enum ContentType : byte
            {
                Xml,
                Json,
            }

            public AsyncLazy<XDocument> Xml
                => _xmlContent ?? throw new InvalidOperationException($"The file '{_path}' is not Xml document.");

            public AsyncLazy<JsonDocument> Json
                => _jsonContent ?? throw new InvalidOperationException($"The file '{_path}' is not Json document.");

            public bool IsLoaded
            {
                get
                {
                    return _type switch
                    {
                        ContentType.Xml => Xml.Task.IsCompleted,
                        _ => Json.Task.IsCompleted,
                    };
                }
            }
        }
    }
}
