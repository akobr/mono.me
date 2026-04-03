using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Utils.Async;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace ApiSdk;

public class TokenService : ITokenService
{
    private readonly IFileSystem _fileSystem;
    private readonly StorytellerSdkOptions _options;
    private readonly AsyncLazy<IPublicClientApplication> _publicClientApp;
    private readonly string[] _scopes;

    public TokenService(IFileSystem fileSystem, IOptions<StorytellerSdkOptions> options)
    {
        _fileSystem = fileSystem;
        _options = options.Value;
        _publicClientApp = new AsyncLazy<IPublicClientApplication>(BuildPublicClientApplication);

        _scopes =
        [
            $"api://{_options.ClientId}/User.Impersonation",
            $"api://{_options.ClientId}/Default.ReadWrite"
        ];
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var pca = await _publicClientApp;
        var accounts = await pca.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        if (account is null)
        {
            return string.Empty;
        }

        try
        {
            var authResult = await pca.AcquireTokenSilent(_scopes, account).ExecuteAsync(ct);
            return authResult.AccessToken;
        }
        catch (MsalException)
        {
            return string.Empty;
        }
    }

    private async Task<IPublicClientApplication> BuildPublicClientApplication()
    {
        var cacheFileName = "msal.cache";
#if DEBUG && !TESTING
        cacheFileName += ".plaintext";
#endif
        var cacheDirectory = _fileSystem.Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net");

        var storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, cacheDirectory)
#if DEBUG && !TESTING
            .WithUnprotectedFile()
#endif
            .Build();

        var pca = PublicClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }
}
