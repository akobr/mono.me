using System.IO;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace c0ded0c.Core.Yaml
{
    public class YamlContent : IContent
    {
        public YamlContent()
        {
            Yaml = string.Empty;
        }

        public YamlContent(string yaml)
        {
            Yaml = yaml;
        }

        public ContentType Type => ContentType.Yaml;

        public string Yaml { get; set; }

        public void FromString(string yaml)
        {
            Yaml = yaml;
        }

        public void From<TObject>(TObject value)
        {
            if (value == null)
            {
                Yaml = string.Empty;
                return;
            }

            var serializer = new SerializerBuilder().Build();
            Yaml = serializer.Serialize(value);
        }

        public TObject? To<TObject>()
            where TObject : class
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<TObject>(Yaml);
        }

        public Task<TObject?> ToAsync<TObject>()
            where TObject : class
        {
            return Task.Run(To<TObject>);
        }

        public async Task WriteToAsync(Stream stream)
        {
            using (TextWriter writer = new StreamWriter(stream, Encoding.UTF8, 4096, true))
            {
                await writer.WriteAsync(Yaml);
            }
        }
    }
}
