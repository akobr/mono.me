# C# SDK

**`42.Platform.Storyteller.Sdk.NSwag`** is the whole Storyteller API as typed C# clients: annotations, configurations, schemas, templates, key/value pairs, access and transfers. It's what `sform` is built on, and what you reach for when the provider and Hosting aren't enough: tools, migrations, admin scripts, integrations.

> Everything the admin UI can do, your code can do too. Sometimes faster. Never with dark mode, sadly.

## Why you'll like it

- **Complete.** Seven typed clients, generated from the same OpenAPI document as the TypeScript SDK.
- **Machine sign-in built in.** API keys, certificates (mTLS), client credentials, token files and workload identity federation, with token caching and renewal.
- **Polling-friendly.** Conditional reads return a `304` as a result, not an exception.
- **Safe writes.** Versioned reads and writes with `If-Match`, so two editors don't overwrite each other.
- **Opt-in resilience.** Retries, a circuit breaker and timeouts, when you want them.

## Install

```bash
dotnet add package 42.Platform.Storyteller.Sdk.NSwag --prerelease
```

## Register and go

```csharp
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;

var endpoint = "https://api.2s.42for.net/api";

// The machine of the STORYTELLER_* variables, bound to its project. Bind once and keep it: it holds the token cache.
var credential = StorytellerCredential.FromEnvironment()!
    .Bind(new StorytellerCredentialBinding(new Uri(endpoint)) { Organization = "house", Project = "main" });

builder.Services.AddStorytellerSdk(
    () => new SdkConfiguration { BaseUrl = endpoint, ClientCertificate = credential.ClientCertificate },
    http => http.AddStorytellerCredential(credential).AddStorytellerResilience());
```

Then inject a client:

```csharp
using _42.Platform.Storyteller.Sdk;

public sealed class CatalogReport(IAnnotationsApiClient annotations)
{
    public async Task PrintAsync(CancellationToken cancellationToken)
    {
        string? token = null;

        do
        {
            var page = await annotations.GetAnnotationsAsync("house", "main", "default", token, cancellationToken: cancellationToken);

            foreach (var annotation in page.Annotations)
            {
                Console.WriteLine($"{annotation.AnnotationKey}  {annotation.Title}");
            }

            token = page.ContinuationToken;
        }
        while (token is not null);
    }
}
```

The clients: `IAccessApiClient`, `IAnnotationsApiClient`, `IConfigurationsApiClient`, `ISchemasApiClient`, `ITemplatesApiClient`, `IKeyValuesApiClient` and `ITransfersApiClient`.

::: tip Optional parameters
Every operation has one method, with the optional parameters last. Pass what you need by name: `GetConfigurationAsync(organization, project, view, key, cancellationToken: cancellationToken)`.
:::

## Pick a credential

| Factory | For |
| --- | --- |
| `FromEnvironment()` | Whatever the `STORYTELLER_*` variables describe; `null` when they describe nothing |
| `FromApiKey(key)` | The simplest machine; `.WithClientCertificate(certificate)` adds mTLS |
| `FromCertificate(certificate)` | A certificate machine; `StorytellerClientCertificate.Load(path, password)` loads the `.pfx` |
| `FromClientCredentials(options)` | A client id and secret of the identity provider |
| `FromTokenFile(path)` | A bearer token in a file that something else keeps fresh |
| `FromFederatedTokenFile(options)` | Workload identity federation: Kubernetes, GitHub Actions and friends, no secret at all |

Signing in as a **person** instead, with a token you already have? Leave the credential out and set `SdkConfiguration.AccessTokenFactory`.

## Poll without the waste

```csharp
var response = await configurations.GetResolvedConfigurationConditionalAsync("house", "main", "default", "sbt.northwind", etag);

if (!response.IsNotModified)
{
    document = response.Result;   // a 200: something changed
}

etag = response.ETag;             // send it back next time, as it is
```

`GetResolvedConfigurationFormattedConditionalAsync(…, "env", etag)` does the same for a [format](formats), as text.

## Write without stepping on toes

```csharp
var read = await annotations.GetAnnotationVersionedAsync("house", "main", "default", "exe.northwind.invoicing.production");
var annotation = read.Result;
annotation.Title = "Invoicing (EU)";

try
{
    await annotations.SetAnnotationVersionedAsync("house", "main", "default", annotation.AnnotationKey, annotation, ifMatch: read.ETag);
}
catch (ApiException<ErrorResponse> e) when (e.StatusCode == 412)
{
    // Someone changed it since the read. Read again, then decide.
}
```

Changing a field or two? `AnnotationJsonPatch` builds a patch, and `PatchAnnotationVersionedAsync` sends it, merging with whatever else changed in the meantime:

```csharp
var patch = new AnnotationJsonPatch()
    .Set(nameof(Annotation.Title), "Invoicing (EU)")
    .SetValue("plan", "pro")
    .AddLabel("eu");

await annotations.PatchAnnotationVersionedAsync("house", "main", "default", "usg.northwind.invoicing", patch.ToJson());
```

## Secrets the platform cannot read

The `Hyok` level ("hold your own key") encrypts a secret **before** it leaves your machine, for the project's registered recipients. The platform stores and versions it, but can't read it. The package `42.Platform.Storyteller.KeyValues.ClientEncryption` does the cryptography (JWE, RSA-OAEP-256 and AES-256-GCM, no dependencies), and the SDK wraps it:

```csharp
using _42.Platform.Storyteller.Sdk.ClientSideEncryption;

var recipients = await keyValues.GetRecipientsAsync("house", "main");
var write = KeyValueClientEncryption.CreateWrite("s3cret", recipients, "house", "main", "default", "db.password");
await keyValues.SetKeyValueAsync("house", "main", "default", "db.password", write);

// Later, by whoever holds the private key of the recipient "api":
var revealed = await keyValues.GetKeyValueAsync("house", "main", "default", "db.password", reveal: true);
var plain = KeyValueClientEncryption.Decrypt(revealed.Value, "api", privateKey);
```

The TypeScript SDK and `sform` read and write exactly the same documents.

## The packages that come along

- **`42.Platform.Storyteller.Abstractions.Annotations`**: the keys and models, in the namespace `_42.Platform.Storyteller`. Handy whenever you build or take apart keys:

  ```csharp
  using _42.Platform.Storyteller;

  var key = AnnotationKey.CreateExecution("northwind", "invoicing", "production");   // exe.northwind.invoicing.production
  var full = FullKey.Parse("@house.main.default.exe.northwind.invoicing.production");

  Console.WriteLine($"{full.Annotation.SubjectName} runs {full.Annotation.ResponsibilityName} in {full.Annotation.ContextName}");
  ```

- **`42.Platform.Storyteller.KeyValues.ClientEncryption`**: the client-side encryption above, usable on its own.

## Good to know

- **Resilience is opt-in.** `AddStorytellerSdk` adds none. If Aspire's service defaults already add `AddStandardResilienceHandler`, don't add ours too, or the retries retry each other.
- **Dates in documents stay strings.** The SDK doesn't guess which strings are dates.
- **One credential, many clients.** Bind it once, as a singleton. Every copy has its own token cache.

## Reference

The [SDK readme](https://github.com/akobr/mono.me/blob/main/src/Platform/Storyteller/Sdk.NSwag/README.md) on GitHub: credentials in depth, federation, token exchange and resilience options. The API itself is described in [Using the platform](../using-the-platform).
