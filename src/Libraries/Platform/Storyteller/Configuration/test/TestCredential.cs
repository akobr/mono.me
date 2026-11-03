using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// A credential of the application: a fixed header, or a failure of a chosen reason. Records its bindings.
internal sealed class TestCredential : StorytellerCredential
{
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly StorytellerAuthenticationFailureReason? _failure;
    private readonly X509Certificate2? _certificate;
    private readonly List<StorytellerCredentialBinding> _bindings;
    private int _calls;

    private TestCredential(
        AuthenticationHeaderValue? authorization,
        StorytellerAuthenticationFailureReason? failure,
        X509Certificate2? certificate,
        List<StorytellerCredentialBinding> bindings)
    {
        _authorization = authorization;
        _failure = failure;
        _certificate = certificate;
        _bindings = bindings;
    }

    public IReadOnlyList<StorytellerCredentialBinding> Bindings => _bindings;

    public int Calls => Volatile.Read(ref _calls);

    public override X509Certificate2? ClientCertificate => _certificate;

    public static TestCredential Bearer(string token, X509Certificate2? certificate = null)
    {
        return new TestCredential(new AuthenticationHeaderValue("Bearer", token), null, certificate, []);
    }

    public static TestCredential ApiKey(string apiKey)
    {
        return new TestCredential(new AuthenticationHeaderValue("ApiKey", apiKey), null, null, []);
    }

    public static TestCredential Failing(StorytellerAuthenticationFailureReason reason)
    {
        return new TestCredential(null, reason, null, []);
    }

    public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        return _failure is { } reason
            ? throw new StorytellerAuthenticationException(reason, $"The test credential fails with {reason}.")
            : ValueTask.FromResult(_authorization);
    }

    public override StorytellerCredential Bind(StorytellerCredentialBinding binding)
    {
        _bindings.Add(binding);
        return this;
    }
}
