using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class JsonContent<TObject> : IContent
        where TObject : class
    {
        public JsonContent(TObject value)
        {
            Value = value;
        }

        public TObject Value { get; set; }

        public ContentType Type => ContentType.Json;

        public TRequestedObject? To<TRequestedObject>()
            where TRequestedObject : class
        {
            return Value as TRequestedObject;
        }

        public Task WriteToAsync(Stream stream)
        {
            return JsonSerializer.SerializeAsync(
                stream,
                Value,
                Value.GetType(),
                new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    IgnoreNullValues = true,
                });
        }
    }
}
