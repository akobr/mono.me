using System.Threading;
using System.Threading.Tasks;

namespace ApiSdk;

public class TokenService: ITokenService
{
    public Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        // Replace with real token acquisition
        return Task.FromResult("your-access-token");
    }
}
