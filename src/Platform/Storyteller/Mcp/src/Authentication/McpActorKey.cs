using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// The RSA key of Mcp:Actor:ClientAssertionKey. It signs the RFC 7523 client assertions of the token exchange, and its
// public part is the JWKS the API's ClientAssertion actor reads. The PKCS#12 file is read on first use, not at start;
// after a failure, again RetryDelay later at the earliest, so the anonymous JWKS cannot make the server read and
// decrypt the file, and log, on every request. A replaced file takes effect after a restart.
public sealed class McpActorKey : IDisposable
{
    public const string JwksPath = "/.well-known/jwks.json";

    public const string Algorithm = SecurityAlgorithms.RsaSha256;

    public const int MinimumKeySize = 2048;

    // Sent at once and used once; the API accepts at most 60 seconds, and 55 keeps a rounded iat or exp within them.
    public static readonly TimeSpan ClientAssertionLifetime = TimeSpan.FromSeconds(55);

    // A failed read is tried again this much later at the earliest; ActorKeyEndpoints sends it as Retry-After.
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    // The default factory caches signature providers process-wide by the key's thumbprint, so a provider would outlive
    // the RSA this class disposes. One assertion per exchange needs no cache.
    private static readonly CryptoProviderFactory UncachedProviders = new() { CacheSignatureProviders = false };

    private readonly McpClientAssertionKeyOptions? _options;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly Lock _lock = new();
    private LoadedKey? _key;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    public McpActorKey(IOptions<McpHostOptions> options, IFileSystem fileSystem, TimeProvider timeProvider, ILogger<McpActorKey> logger)
    {
        var actor = options.Value.Actor;
        _options = actor.HasClientAssertionKey ? actor.ClientAssertionKey : null;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsConfigured => _options is not null;

    // iss = sub = the actor id, aud = the audience (the token endpoint, or Mcp:Actor:ClientAssertionKey:Audience), a new
    // jti, iat, nbf and exp; RS256 with the kid of the JWKS.
    // StorytellerActorException when the key cannot be read.
    public string CreateClientAssertion(string actorId, string audience, DateTimeOffset now)
    {
        var key = GetKey();

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = actorId,
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = actorId,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = (now + ClientAssertionLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(key.SigningKey, Algorithm),
        });
    }

    // StorytellerActorException when the key cannot be read.
    public JsonWebKeySetDocument GetJsonWebKeySet()
    {
        return new JsonWebKeySetDocument { Keys = [GetKey().PublicKey] };
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _key?.Dispose();
            _key = null;
        }
    }

    // RFC 7638: the SHA-256 thumbprint of the public key, base64url encoded, as the API names its own keys.
    internal static string ComputeKeyId(RSAParameters publicKey)
    {
        return Base64UrlEncoder.Encode(new RsaSecurityKey(publicKey).ComputeJwkThumbprint());
    }

    private static StorytellerActorException Unavailable()
    {
        return new StorytellerActorException(StorytellerActorException.CredentialUnavailableMessage, "actor_key_unavailable");
    }

    private LoadedKey GetKey()
    {
        if (_options is null)
        {
            throw new InvalidOperationException("Mcp:Actor:ClientAssertionKey is not set.");
        }

        lock (_lock)
        {
            if (_key is not null)
            {
                return _key;
            }

            var now = _timeProvider.GetUtcNow();

            // Within the back-off of a failed read: no read and no log.
            if (now < _retryAt)
            {
                throw Unavailable();
            }

            try
            {
                return _key = Load(_options);
            }
            catch (StorytellerActorException)
            {
                _retryAt = now + RetryDelay;
                throw;
            }
        }
    }

    private LoadedKey Load(McpClientAssertionKeyOptions options)
    {
        X509Certificate2? certificate = null;
        RSA? rsa = null;

        try
        {
            certificate = X509CertificateLoader.LoadPkcs12(_fileSystem.File.ReadAllBytes(options.CertificatePath!), options.CertificatePassword);
            rsa = certificate.GetRSAPrivateKey();

            if (rsa is null || rsa.KeySize < MinimumKeySize)
            {
                throw new CryptographicException($"The certificate holds no RSA private key of at least {MinimumKeySize} bits.");
            }

            var publicKey = rsa.ExportParameters(includePrivateParameters: false);
            var keyId = ComputeKeyId(publicKey);

            return new LoadedKey(
                certificate,
                rsa,
                new RsaSecurityKey(rsa) { KeyId = keyId, CryptoProviderFactory = UncachedProviders },
                new PublicJsonWebKey
                {
                    Kid = keyId,
                    N = Base64UrlEncoder.Encode(publicKey.Modulus),
                    E = Base64UrlEncoder.Encode(publicKey.Exponent),
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            rsa?.Dispose();
            certificate?.Dispose();

            // The exception type only, as everywhere in the server's logs.
            _logger.LogWarning("The client assertion key of Mcp:Actor could not be read ({ExceptionType}).", exception.GetType().Name);
            throw Unavailable();
        }
    }

    private sealed class LoadedKey(X509Certificate2 certificate, RSA rsa, RsaSecurityKey signingKey, PublicJsonWebKey publicKey) : IDisposable
    {
        public RsaSecurityKey SigningKey { get; } = signingKey;

        public PublicJsonWebKey PublicKey { get; } = publicKey;

        public void Dispose()
        {
            rsa.Dispose();
            certificate.Dispose();
        }
    }
}
