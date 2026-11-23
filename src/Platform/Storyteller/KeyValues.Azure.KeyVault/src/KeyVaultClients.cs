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
    // managedIdentityClientId: in production the client id of the user-assigned identity the app runs as (AZURE_CLIENT_ID);
    // without it the system-assigned identity, which an app with only a user-assigned identity does not have.
    public static KeyClient CreateKeyClient(Uri vaultUri, bool emulator, bool isProduction, string? managedIdentityClientId = null)
    {
        ArgumentNullException.ThrowIfNull(vaultUri);
        var options = new KeyClientOptions();

        if (emulator)
        {
            if (isProduction)
            {
                throw new InvalidOperationException("The Key Vault emulator must not be used in production (KeyValues:Protection:Emulator).");
            }

            options.DisableChallengeResourceVerification = true;
            options.Transport = CreateEmulatorTransport();
            return new KeyClient(vaultUri, new EmulatorTokenCredential(), options);
        }

        return new KeyClient(vaultUri, CreateCredential(isProduction, managedIdentityClientId), options);
    }

    // The credential of a real vault: in production the managed identity (the user-assigned one of
    // managedIdentityClientId when given), then the environment's service principal; elsewhere DefaultAzureCredential.
    public static TokenCredential CreateCredential(bool isProduction, string? managedIdentityClientId)
    {
        if (!isProduction)
        {
            return new DefaultAzureCredential();
        }

        return new ChainedTokenCredential(
            new ManagedIdentityCredential(GetManagedIdentityId(managedIdentityClientId)),
            new EnvironmentCredential());
    }

    // The identity a production client asks the platform for: the user-assigned identity of the client id, otherwise the
    // system-assigned one. A blank client id counts as none, and the id is used without the white space around it.
    public static ManagedIdentityId GetManagedIdentityId(string? managedIdentityClientId)
    {
        return string.IsNullOrWhiteSpace(managedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId.Trim());
    }

    // A transport over the shared emulator handler; a key client never disposes its transport, and the handler outlives it.
    internal static HttpPipelineTransport CreateEmulatorTransport()
    {
        return new HttpClientTransport(new HttpClient(SharedEmulatorHandler.Value, disposeHandler: false));
    }
}
