# Storyteller configuration provider for .NET

Loads a configuration of the 2S Platform Storyteller into `IConfiguration`. The provider polls it with `If-None-Match`, keeps a last-known-good cache, switches the active key at runtime and resolves secret references locally. `IStorytellerDocuments` reads any configuration on demand, for jobs.

## Install

```powershell
dotnet add package 42.Platform.Storyteller.Configuration --prerelease
```

## Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// STORYTELLER_ENDPOINT, STORYTELLER_KEY and the variables of the machine's credential come from the environment.
builder.Configuration.AddStoryteller();

// The provider's logging, and IStorytellerConfigurationRefresher in the container.
builder.Services.AddStorytellerConfiguration();

// IOptionsMonitor<InvoicingOptions> follows every applied document.
builder.Services.Configure<InvoicingOptions>(builder.Configuration.GetSection("invoicing"));

var app = builder.Build();
app.Run();
```

The environment of a machine with an API key:

```text
STORYTELLER_ENDPOINT=https://api.2s.42for.net/api
STORYTELLER_KEY=@house.main.default.exe.northwind.invoicing.production
STORYTELLER_API_KEY=2s.…
```

- The options can also be set in code: `builder.Configuration.AddStoryteller(options => { … })`. A value set in code wins over the environment.
- A required configuration that cannot be read stops the start. `STORYTELLER_OPTIONAL=true` starts without the platform when it is unreachable and nothing is cached.
- `STORYTELLER_CACHE_DIR` turns on the last-known-good cache on disk.
- Secret references (`kv://…`, Key Vault secret URIs) are resolved by a resolver in `options.ReferenceResolver`, for example the one of `42.Platform.Storyteller.Configuration.KeyVault`.

## Documentation

The settings and their variables, startup, refresh and key switching, the cache and its protection, secret references and documents on demand: [Configuration Provider for .NET](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-provider.md).
