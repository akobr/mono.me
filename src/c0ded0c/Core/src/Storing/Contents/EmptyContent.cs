using System.IO;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class EmptyContent : IContent
    {
        public ContentType Type => ContentType.Empty;

        public TObject? To<TObject>()
            where TObject : class
        {
            return default(TObject);
        }

        public Task WriteToAsync(Stream stream)
        {
            return Task.CompletedTask;
        }
    }
}
