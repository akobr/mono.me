using Azure;
using Azure.Security.KeyVault.Secrets;

namespace _42.Platform.Storyteller.Sdk.Configuring.KeyVault.UnitTests;

// A SecretClient of one vault without a network: answers from Respond, built with SecretModelFactory, and records every
// read with the caller's token.
internal sealed class StubSecretClient : SecretClient
{
    private readonly List<Read> _reads = [];
    private readonly Lock _lock = new();

    public StubSecretClient(Uri vaultUri)
    {
        VaultUri = vaultUri;
    }

    public override Uri VaultUri { get; }

    public Func<string, string?, CancellationToken, Task<KeyVaultSecret>> Respond { get; set; } = (name, version, _) => Task.FromResult(Secret(name, version, $"value of {name}"));

    public IReadOnlyList<Read> Reads
    {
        get
        {
            lock (_lock)
            {
                return [.. _reads];
            }
        }
    }

    public static KeyVaultSecret Secret(string name, string? version, string? value)
    {
        var properties = SecretModelFactory.SecretProperties(name: name, version: version ?? "0123456789abcdef0123456789abcdef");
        return SecretModelFactory.KeyVaultSecret(properties, value);
    }

    public override async Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _reads.Add(new Read(name, version, cancellationToken));
        }

        var secret = await Respond(name, version, cancellationToken);
        return Response.FromValue(secret, null!);
    }

    internal sealed record Read(string Name, string? Version, CancellationToken CancellationToken);
}
