using System;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

public class AuthenticationServiceSelectorTests
{
    [Fact]
    public async Task AuthKitSettings_UseTheAuthKitSessionStore()
    {
        var resolver = new FixedResolver(AuthenticationProvider.AuthKit);
        var store = new InMemoryTokenStore
        {
            Session = new AuthKitSession
            {
                AccessToken = "access-fresh",
                RefreshToken = "refresh-1",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                User = new AuthKitSessionUser("user_01", "ada@example.com", null, null),
            },
        };
        using var selector = new AuthenticationServiceSelector(resolver, new FileSystem(), new StubHttpClientFactory(new ScriptedHttpHandler()), store);

        (await selector.GetAccessTokenAsync()).ShouldBe("access-fresh");
        (await selector.GetSignedInUserAsync()).ShouldBe(new SignedInUser("user_01", "ada@example.com", null));
        resolver.Resolutions.ShouldBe(1);
    }

    [Fact]
    public async Task Logout_AlsoForgetsTheDiscoveredProvider()
    {
        var resolver = new FixedResolver(AuthenticationProvider.AuthKit);
        var store = new InMemoryTokenStore();
        using var selector = new AuthenticationServiceSelector(resolver, new FileSystem(), new StubHttpClientFactory(new ScriptedHttpHandler()), store);

        await selector.LogoutAsync();

        store.Clears.ShouldBe(1);
        resolver.Forgets.ShouldBe(1);
    }

    [Fact]
    public async Task ResolutionFailure_ReachesTheCaller()
    {
        var resolver = new FixedResolver(AuthenticationProvider.AuthKit, new AuthenticationException(AuthenticationFailureReason.ServiceError, "no provider"));
        using var selector = new AuthenticationServiceSelector(resolver, new FileSystem(), new StubHttpClientFactory(new ScriptedHttpHandler()), new InMemoryTokenStore());

        var exception = await Should.ThrowAsync<AuthenticationException>(() => selector.GetAccessTokenAsync());

        exception.Message.ShouldBe("no provider");
    }

    private sealed class FixedResolver : IAuthenticationConfigurationResolver
    {
        private readonly AuthenticationProvider _provider;
        private readonly Exception? _failure;

        public FixedResolver(AuthenticationProvider provider, Exception? failure = null)
        {
            _provider = provider;
            _failure = failure;
        }

        public int Resolutions { get; private set; }

        public int Forgets { get; private set; }

        public Task<ResolvedAuthentication> ResolveAsync(CancellationToken cancellationToken = default)
        {
            Resolutions++;
            return _failure is null
                ? Task.FromResult(new ResolvedAuthentication { Provider = _provider, ClientId = "client_123" })
                : Task.FromException<ResolvedAuthentication>(_failure);
        }

        public Task ForgetAsync(CancellationToken cancellationToken = default)
        {
            Forgets++;
            return Task.CompletedTask;
        }
    }
}
