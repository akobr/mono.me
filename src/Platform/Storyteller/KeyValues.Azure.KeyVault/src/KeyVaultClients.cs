using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;

namespace _42.Platform.Storyteller.KeyValues;

public static class KeyVaultClients
{
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
            options.Transport = new HttpClientTransport(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
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
}
