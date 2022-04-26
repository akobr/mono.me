using System.IO;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IContent
    {
        public ContentType Type { get; }

        Task WriteToAsync(Stream stream);

        TObject? To<TObject>()
            where TObject : class;
    }
}
