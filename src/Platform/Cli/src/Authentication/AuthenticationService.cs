using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Configuration;
using _42.Utils.Async;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Authentication;

public class AuthenticationService : IAuthenticationService
{
    private readonly IFileSystem _fileSystem;
    private readonly AuthenticationOptions _options;
    private readonly AsyncLazy<IPublicClientApplication> _publicClientApp;

    private readonly string[] _scopes;

    public AuthenticationService(
        IFileSystem fileSystem,
        IOptions<AuthenticationOptions> options)
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
            .Create(_options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
            .WithDefaultRedirectUri()
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }
}
