using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _42.Monorepo.Cli.Cache
{
    public interface IFileContentCache
    {
        bool IsLoaded(string filePath);

        Task<XDocument> GetOrLoadXmlContentAsync(string filePath, CancellationToken cancellationToken = default);

        Task<JsonDocument> GetOrLoadJsonContentAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
