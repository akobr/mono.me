using System.IO;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace c0ded0c.Core.Yaml
{
    public class YamlContent<TObject> : IContent
        where TObject : class
    {
        public YamlContent(TObject value)
        {
            Value = value;
        }

        public ContentType Type => ContentType.Yaml;

        public TObject Value { get; set; }

        public TRequestedObject? To<TRequestedObject>()
            where TRequestedObject : class
        {
            return Value as TRequestedObject;
        }

        public Task WriteToAsync(Stream stream)
        {
            return Task.Run(() => WriteTo(stream));
        }

        public void WriteTo(Stream stream)
        {
            using (TextWriter writer = new StreamWriter(stream, Encoding.UTF8, 4096, true))
            {
                var serialiser = new SerializerBuilder().Build();
                serialiser.Serialize(writer, Value);
            }
        }
    }
}
