using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;

namespace _42.Platform.Storyteller.KeyValues;

public static class KeyVaultClients
{
    // One handler for every emulator client of the process: it connects "localhost" over IPv4 first and accepts the
    // self-signed certificate (EmulatorHttpHandler); Lowkey Vault is published on 127.0.0.1 only.
    private static readonly Lazy<SocketsHttpHandler> SharedEmulatorHandler = new(EmulatorHttpHandler.Create);

    // emulator: a dummy token, no challenge resource check, and any server certificate (Lowkey Vault is self-signed).
    public static KeyClient CreateKeyClient(Uri vaultUri, bool emulator, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(vaultUri);
        var options = new KeyClientOptions();
        TokenCredential credential;

        if (emulator)
        {
            if (isProduction)
            {
                throw new InvalidOperationException("The Key Vault emulator must not be used in production (KeyValues:Protection:Emulator).");
            }

            options.DisableChallengeResourceVerification = true;
            options.Transport = CreateEmulatorTransport();
            credential = new EmulatorTokenCredential();
        }
        else if (isProduction)
        {
            credential = new ChainedTokenCredential(new ManagedIdentityCredential(), new EnvironmentCredential());
        }
        else
        {
            credential = new DefaultAzureCredential();
        }

        return new KeyClient(vaultUri, credential, options);
    }

    // A transport over the shared emulator handler; a key client never disposes its transport, and the handler outlives it.
    internal static HttpPipelineTransport CreateEmulatorTransport()
    {
        return new HttpClientTransport(new HttpClient(SharedEmulatorHandler.Value, disposeHandler: false));
    }
}
