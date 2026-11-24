---
pageClass: dev-guide
---

# Key Vault secrets

**`42.Platform.Storyteller.Configuration.KeyVault`** lets your configuration *point at* a secret in your Azure Key Vault, and lets **your app** fetch it, with **its own** identity. The platform stores the address of the secret, never the secret.

> The catalog knows where the treasure is buried. Only your app has the shovel.

## Why you'll like it

- **Your vault, your rules.** Secrets stay in the Key Vault you already have, under the access policies you already audit.
- **No secret in transit through the platform.** The resolved document carries the reference; the provider swaps in the value inside your process.
- **Rotation for free.** References are resolved again every hour (by default), so a rotated secret reaches your app without a deployment.
- **Never leaks.** Resolved values are never logged, cached on disk or put in an exception.

## Install

```bash
dotnet add package 42.Platform.Storyteller.Configuration.KeyVault --prerelease
dotnet add package Azure.Identity
```

The package doesn't pull in `Azure.Identity` itself: you pick the credential.

## Point at a secret

Store a key/value pair as a reference, and bind it in the configuration. In a resolved document it arrives as plain text, in one of these shapes:

| Form | Example |
| --- | --- |
| Shorthand | `kv://contoso-vault/db-password` |
| Secret URI | `https://contoso-vault.vault.azure.net/secrets/db-password` |
| App Configuration reference | `{"uri":"https://contoso-vault.vault.azure.net/secrets/db-password"}` |

A version at the end (`kv://contoso-vault/db-password/<32 hex characters>`) pins one version; without it you get the latest.

## Let your app dig

```csharp
using _42.Platform.Storyteller.Sdk.Configuring.KeyVault;
using Azure.Identity;

builder.Configuration.AddStoryteller(options =>
{
    options.ReferenceResolver = new KeyVaultReferenceResolver(
        new DefaultAzureCredential(),
        new KeyVaultReferenceResolverOptions { AllowedVaults = { "contoso-vault" } });
});
```

That's it. `builder.Configuration["db:password"]` is now the secret itself, and `IOptionsMonitor<T>` binds it like any other value.

- **`AllowedVaults`** keeps the resolver to the vaults you name. Set it: see the tip below.
- **`SecretRefreshInterval`** (on the provider's options) is how old a resolved value may get, 1 hour by default.
- **Sovereign clouds** and emulators go into `AllowedHostSuffixes`. The [reference](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-provider.md#the-key-vault-resolver) shows a local Lowkey Vault setup.

::: tip Least privilege, please
Anyone who can edit the configuration can make your app read any secret your app's identity can read, and put it under a key of their choice. So give the identity `Key Vault Secrets User` on the vaults it needs, no more, and list them in `AllowedVaults`.
:::

## When it can't dig

| What happens | Result |
| --- | --- |
| Key Vault is throttling or down (`429`, `5xx`, network) | Transient: a refresh keeps the previous document, a startup counts it like an unreachable platform |
| The identity may not read the secret, or it doesn't exist (`401`, `403`, `404`) | Definitive: the document is not applied, and the message names the vault and the secret |
| A `kv://` reference of a vault outside `AllowedVaults` | Refused, never applied as text |

There is no fallback to the text of the reference: your app never accidentally uses `kv://contoso-vault/db-password` as a password.

## Good to know

- **Only Storyteller documents are resolved.** A reference in `appsettings.json` or an environment variable stays a string.
- **The contract is neutral.** `ISecretReferenceResolver` knows no vault; an AWS, Google or HashiCorp resolver can implement it, and `SecretReferenceResolver.FirstOf(a, b)` chains several.
- **`effective` mode resolves nothing.** References are resolved only in the default `resolved` mode.

## Reference

[Secret references](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-provider.md#secret-references) in the provider's documentation, and [key/value pairs](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/key-values.md#references) for storing references on the platform.
