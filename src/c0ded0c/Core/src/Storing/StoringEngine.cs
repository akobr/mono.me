using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using c0ded0c.Core.Configuration;

namespace c0ded0c.Core
{
    public class StoringEngine : IStoringEngine
    {
        private readonly IStorer storer;
        private readonly IImmutableDictionary<string, string> properties;
        private ImmutableDictionary<ContentType, Func<Stream, Task<IContent>>> retrieveStrategies;

        public StoringEngine(IStorer storer, IImmutableDictionary<string, string> properties)
        {
            this.storer = storer ?? throw new ArgumentNullException(nameof(storer));
            this.properties = properties ?? throw new ArgumentNullException(nameof(properties));

            retrieveStrategies = ImmutableDictionary.CreateRange(new Dictionary<ContentType, Func<Stream, Task<IContent>>>
            {
                { ContentType.Json, LoadJson },

                // TODO: [P3] Add support for more types
                { ContentType.Xml, (stream) => LoadString(stream, ContentType.Xml) },
                { ContentType.Yaml, (stream) => LoadString(stream, ContentType.Yaml) },
                { ContentType.PlantUml, (stream) => LoadString(stream, ContentType.PlantUml) },
                { ContentType.Markdown, (stream) => LoadString(stream, ContentType.Markdown) },
                { ContentType.Html, (stream) => LoadString(stream, ContentType.Html) },
                { ContentType.Text, (stream) => LoadString(stream, ContentType.Text) },
            });

            SetWorkingDirectory();
        }

        public async Task<TObject?> LoadArtifactAsync<TObject>(IArtifactInfo artifact)
            where TObject : class
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (artifact.Type == ContentType.Json)
            {
                throw new ArgumentException("Load of a concreate object type is available only for Json.", nameof(artifact));
            }

            artifact.SetContent(await LoadContentAsync(artifact.Type, artifact.Path));
            return artifact.Content.To<TObject>();
        }

        public async Task LoadArtifactAsync(IArtifactInfo artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            artifact!.SetContent(await LoadContentAsync(artifact.Type, artifact.Path));
        }

        public async Task StoreArtifactAsync(IArtifactInfo artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (!artifact.HasContent)
            {
                throw new ArgumentException("An artifact must have a content.", nameof(artifact));
            }

            using (Stream target = storer.GetOrCreateStream(artifact!.Path))
            {
                await artifact.Content.WriteToAsync(target);
            }
        }

        public async Task<IContent> LoadContentAsync(ContentType type, string path)
        {
            Stream? source = storer.GetStream(path);

            if (source == null
                || !retrieveStrategies.TryGetValue(type, out var retrieveStrategy))
            {
                return new EmptyContent();
            }

            return await retrieveStrategy(source);
        }

        public void RegisterRetrievingStrategy(ContentType type, Func<Stream, Task<IContent>> strategy)
        {
            retrieveStrategies = retrieveStrategies.SetItem(type, strategy);
        }

        private async Task<IContent> LoadString(Stream stream, ContentType type = ContentType.Text)
        {
            using (TextReader reader = new StreamReader(stream))
            {
                return new StringContent(await reader.ReadToEndAsync(), type);
            }
        }

        private async Task<IContent> LoadJson(Stream stream)
        {
            // TODO: [P4] Check it this array pool make sense
            ArrayPool<byte> arrayPool = ArrayPool<byte>.Shared;
            byte[] json = arrayPool.Rent(4096);
            int size = 0, readSize;

            using (stream)
            {
                do
                {
                    readSize = await stream.ReadAsync(json, size, json.Length - size);
                    size += readSize;

                    if (size == json.Length)
                    {
                        byte[] tmp = json;
                        json = arrayPool.Rent(size * 2);
                        Array.Copy(tmp, json, size);
                        arrayPool.Return(tmp);
                    }
                }
                while (readSize > 0);
            }

            byte[] resultJson = new byte[size];
            Array.Copy(json, resultJson, size);
            arrayPool.Return(json);

            return new JsonContent(json.AsMemory(0, size));
        }

        private void SetWorkingDirectory()
        {
            storer.SetWorkingPath(
                Path.Combine(
                    properties.Get(PropertyNames.WorkingDirectory, Constants.CURRENT_DIRECTORY),
                    properties.Get(PropertyNames.RunName, Constants.SPARE_RUN_NAME)));
        }
    }
}
