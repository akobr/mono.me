using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Utils.Async;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Authentication;

// Entra ID sign-in through MSAL, with the token cache in ~/.42for.net/msal.cache.
public class EntraIdAuthenticationService : IAuthenticationService
{
    private readonly IFileSystem _fileSystem;
    private readonly ResolvedAuthentication _settings;
    private readonly AsyncLazy<IPublicClientApplication> _publicClientApp;
    private readonly string[] _scopes;

    public EntraIdAuthenticationService(IFileSystem fileSystem, ResolvedAuthentication settings)
    {
        _fileSystem = fileSystem;
        _settings = settings;
        _publicClientApp = new AsyncLazy<IPublicClientApplication>(BuildPublicClientApplication);

        _scopes = settings.Scopes.Count > 0
            ? settings.Scopes.ToArray()
            : [$"api://{settings.ClientId}/User.Impersonation", $"api://{settings.ClientId}/Default.ReadWrite"];
    }

    public async Task<SignedInUser?> GetSignedInUserAsync(CancellationToken cancellationToken = default)
    {
        var result = await AcquireSilentAsync(cancellationToken);
        return result is null ? null : ToUser(result);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var result = await AcquireSilentAsync(cancellationToken);
        return result?.AccessToken;
    }

    public async Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        CancellationToken cancellationToken = default)
    {
        var pca = await _publicClientApp;

        try
        {
            var result = await pca.AcquireTokenWithDeviceCode(
                    _scopes,
                    deviceCode => onPrompt(new DeviceCodePrompt(
                        deviceCode.UserCode,
                        new Uri(deviceCode.VerificationUrl),
                        null,
                        deviceCode.ExpiresOn - DateTimeOffset.UtcNow)))
                .ExecuteAsync(cancellationToken);

            return ToUser(result);
        }
        catch (OperationCanceledException exception)
        {
            throw new AuthenticationException(AuthenticationFailureReason.Cancelled, "The sign-in was cancelled.", exception);
        }
        catch (MsalServiceException exception) when (exception.ErrorCode is "authorization_declined" or "access_denied")
        {
            throw new AuthenticationException(AuthenticationFailureReason.Denied, "The sign-in was declined.", exception);
        }
        catch (MsalServiceException exception) when (exception.ErrorCode is "code_expired" or "expired_token")
        {
            throw new AuthenticationException(AuthenticationFailureReason.Expired, "The sign-in code expired.", exception);
        }
        catch (MsalServiceException exception)
        {
            throw new AuthenticationException(AuthenticationFailureReason.ServiceError, exception.Message, exception);
        }
        catch (MsalClientException exception)
        {
            // MSAL reports a device code that ran out before the user finished as a client error.
            throw new AuthenticationException(AuthenticationFailureReason.Expired, "The sign-in timed out.", exception);
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var pca = await _publicClientApp;

        foreach (var account in await pca.GetAccountsAsync())
        {
            await pca.RemoveAsync(account);
        }
    }

    private static SignedInUser ToUser(AuthenticationResult result)
    {
        var name = result.ClaimsPrincipal?.Claims.FirstOrDefault(claim => claim.Type == "name")?.Value;
        return new SignedInUser(result.UniqueId ?? result.Account.HomeAccountId.ObjectId, result.Account.Username, name);
    }

    private async Task<AuthenticationResult?> AcquireSilentAsync(CancellationToken cancellationToken)
    {
        var pca = await _publicClientApp;
        var account = (await pca.GetAccountsAsync()).FirstOrDefault();

        if (account is null)
        {
            return null;
        }

        try
        {
            return await pca.AcquireTokenSilent(_scopes, account).ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException)
        {
            return null;
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
            .Create(_settings.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _settings.TenantId)
            .Build();

        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }
}
