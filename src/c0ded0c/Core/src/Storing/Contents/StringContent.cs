using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class StringContent : IContent
    {
        private readonly string text;

        public StringContent(string text)
        {
            this.text = text;
            Type = ContentType.Text;
        }

        public StringContent(string content, ContentType type)
        {
            text = content;
            Type = type;
        }

        public ContentType Type { get; }

        public TObject? To<TObject>()
            where TObject : class
        {
            // TODO: [P3] Check for Parse/TryParse and IFormatProvider argument and call this static method
            return default(TObject);
        }

        public async Task WriteToAsync(Stream stream)
        {
            using (TextWriter writer = new StreamWriter(stream, Encoding.UTF8, 4096, true))
            {
                await writer.WriteAsync(text);
            }
        }
    }
}
