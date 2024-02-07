using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Utils.Async;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly IFileSystem _fileSystem;
    private readonly AsyncLazy<IPublicClientApplication> _publicClientApp;

    private readonly string[] _scopes = {
        "api://7f37b203-3599-4c73-9796-39d96883198c/User.Impersonation",
        "api://7f37b203-3599-4c73-9796-39d96883198c/Default.ReadWrite",
    };

    public AuthenticationService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _publicClientApp = new AsyncLazy<IPublicClientApplication>(BuildPublicClientApplication);
    }

    public string[] Scopes => _scopes;

    public async Task<IPublicClientApplication> GetPublicClientApplicationAsync()
    {
        return await _publicClientApp;
    }

    public async Task<AuthenticationResult?> GetAuthenticationAsync()
    {
        var pca = await _publicClientApp;
        var accounts = await pca.GetAccountsAsync();

        return await pca.AcquireTokenSilent(
                _scopes,
                accounts.FirstOrDefault())
            .ExecuteAsync();
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
            .Create("7f37b203-3599-4c73-9796-39d96883198c")
            .WithAuthority(AzureCloudInstance.AzurePublic, "f2b1a691-0cf0-47d2-9b2c-b13cb7b6467e")
            .WithDefaultRedirectUri()
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }
}
