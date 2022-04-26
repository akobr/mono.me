using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class JsonContent : IContent
    {
        public JsonContent()
        {
            Json = Memory<byte>.Empty;
        }

        public JsonContent(ReadOnlyMemory<byte> json)
        {
            Json = json;
        }

        public ReadOnlyMemory<byte> Json { get; set; }

        public ContentType Type => ContentType.Json;

        public void FromString(string json)
        {
            Json = Encoding.UTF8.GetBytes(json);
        }

        public Task FromStringAsync(string json)
        {
            return Task.Run(() => FromString(json));
        }

        public void From<TObject>(TObject value)
        {
            Json = JsonSerializer.SerializeToUtf8Bytes<TObject>(value);
        }

        public Task FromAsync<TObject>(TObject value)
        {
            return Task.Run(() => From<TObject>(value));
        }

        public TObject? To<TObject>()
            where TObject : class
        {
            return JsonSerializer.Deserialize<TObject?>(Json.Span);
        }

        public Task<TObject?> ToAsync<TObject>()
            where TObject : class
        {
            return Task.Run(To<TObject>);
        }

        public Task WriteToAsync(Stream stream)
        {
            return stream.WriteAsync(Json).AsTask();
        }
    }
}
