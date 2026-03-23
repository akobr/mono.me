using System.Threading;
using System.Threading.Tasks;

namespace ApiSdk;

public interface ITokenService
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}
