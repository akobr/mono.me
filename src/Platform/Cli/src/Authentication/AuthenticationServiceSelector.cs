using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using _42.Utils.Async;

namespace _42.Platform.Cli.Authentication;

// The registered IAuthenticationService. The provider is known only after discovery, so the
// Entra ID or AuthKit implementation is created on first use; commands that never sign in
// never call the server.
public sealed class AuthenticationServiceSelector : IAuthenticationService, IDisposable
{
    private readonly IAuthenticationConfigurationResolver _resolver;
    private readonly AsyncLazy<IAuthenticationService> _inner;

    public AuthenticationServiceSelector(
        IAuthenticationConfigurationResolver resolver,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory,
        ITokenStore tokenStore)
    {
        _resolver = resolver;
        _inner = new AsyncLazy<IAuthenticationService>(async () =>
        {
            var settings = await resolver.ResolveAsync();
            return settings.Provider switch
            {
                AuthenticationProvider.AuthKit => new AuthKitAuthenticationService(
                    httpClientFactory.CreateClient(nameof(AuthKitAuthenticationService)),
                    tokenStore,
                    settings),
                _ => new EntraIdAuthenticationService(fileSystem, settings),
            };
        });
    }

    public async Task<SignedInUser?> GetSignedInUserAsync(CancellationToken cancellationToken = default)
    {
        return await (await _inner).GetSignedInUserAsync(cancellationToken);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return await (await _inner).GetAccessTokenAsync(cancellationToken);
    }

    public async Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        CancellationToken cancellationToken = default)
    {
        return await (await _inner).LoginWithDeviceCodeAsync(onPrompt, selectOrganization, cancellationToken);
    }

    // Also forgets the discovered provider, so the next sign-in follows a server that switched.
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await (await _inner).LogoutAsync(cancellationToken);
        await _resolver.ForgetAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_inner.IsValueCreated && _inner.Value.IsCompletedSuccessfully && _inner.Value.Result is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
