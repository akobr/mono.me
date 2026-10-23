# Storyteller Key Vault references for .NET

Resolves the secret references of 2S Platform Storyteller configurations in Azure Key Vault, locally, with the application's own identity. It is the `ReferenceResolver` of the configuration provider `42.Platform.Storyteller.Configuration`, and understands the platform's three forms: `kv://{vault}/{name}[/{version}]`, a vault secret URI, and App Configuration's `{"uri": …}`.

## Install

```powershell
dotnet add package 42.Platform.Storyteller.Configuration.KeyVault --prerelease
dotnet add package Azure.Identity
```

## Use

```csharp
using _42.Platform.Storyteller.Sdk.Configuring.KeyVault;
using Azure.Identity;

builder.Configuration.AddStoryteller(options =>
{
    options.ReferenceResolver = new KeyVaultReferenceResolver(
        new DefaultAzureCredential(),
        new KeyVaultReferenceResolverOptions { AllowedVaults = { "invoicing-prod" } });
});
```

- References are resolved in resolved reads only (`STORYTELLER_MODE=resolved`, the default).
- Whoever can write the configuration can make the application read any secret that its identity can read. Set `AllowedVaults`, and give the identity read access only to the secrets it needs.
- `AllowedHostSuffixes` admits sovereign clouds and the host of a local emulator; `DefaultVaultSuffix` is the host suffix of the `kv://` shorthand. A secret URI names a host on the default `https` port, without a port: for an emulator on another port, put the port back in `SecretClientFactory`. [Lowkey Vault](https://github.com/nagyesta/lowkey-vault) also needs a dummy token credential, `DisableChallengeResourceVerification` and its self-signed certificate accepted, in local development only; the documentation has an example.

## Documentation

[Configuration Provider for .NET](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-provider.md#the-key-vault-resolver).
