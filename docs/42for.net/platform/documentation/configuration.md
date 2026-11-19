# Configuration provider

**`42.Platform.Storyteller.Configuration`** puts the configuration of one key into your `IConfiguration`, and keeps it fresh while the app runs. It's the package most services need, and usually the only one.

> Like `appsettings.json`, except it lives in one place, knows which customer you are, and doesn't need a redeploy to change its mind.

## Why you'll like it

- **Two lines.** `AddStoryteller()` and you're done. Everything you already use, `IOptions<T>`, `IOptionsMonitor<T>`, `GetSection`, keeps working.
- **Live.** It polls with an `ETag`, so an unchanged document costs a `304` and nothing else. A change reaches `IOptionsMonitor<T>` without a restart.
- **Calm in a storm.** The platform is down? Your app keeps the last good document in memory, and with a cache directory it even **starts** without the platform.
- **Bad documents bounce.** A document that fails your options validation is rolled back, and your app keeps running on the previous one.
- **Secrets stay yours.** Add [Key Vault](key-vault) and secret references are resolved by your app, with your app's identity.

## Install

:::tabs
== .NET CLI
```bash
dotnet add package 42.Platform.Storyteller.Configuration --prerelease
```
== Project file
```xml
<PackageReference Include="42.Platform.Storyteller.Configuration" Version="0.10.*-*" />
```
:::

## The smallest thing that works

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddStoryteller();           // the configuration of STORYTELLER_KEY
builder.Services.AddStorytellerConfiguration();   // its logging, and the refresher in the container

builder.Services.Configure<InvoicingOptions>(builder.Configuration.GetSection("invoicing"));
```

No `using` needed: the extensions live in the `Microsoft.Extensions.*` namespaces, next to their cousins.

It reads three things from the environment:

| Variable | What | Example |
| --- | --- | --- |
| `STORYTELLER_ENDPOINT` | The API, `/api` included | `https://api.2s.42for.net/api` |
| `STORYTELLER_KEY` | Who you are: the full key of any annotation | `@house.main.default.exe.northwind.invoicing.production` |
| A credential | The machine's sign-in, as `sform account machine create` printed it | `STORYTELLER_API_KEY=2s.…` |

Anything missing or malformed fails at startup with a message that names the variable, never its value. Better a loud start than a quiet wrong one.

::: tip The same variables everywhere
`sform`, the C# SDK, Hosting and the GitHub Action read exactly the same `STORYTELLER_*` names. One set of variables, many tools. The [cheat sheet](cheat-sheet#the-storyteller-variables) lists them all.
:::

## Settings in code

Prefer code to variables? Set what you like; anything you leave out still comes from the environment:

```csharp
using _42.Platform.Storyteller.Sdk.Configuring;

builder.Configuration.AddStoryteller(options =>
{
    options.Key = "@house.main.default.exe.northwind.invoicing.production";
    options.RefreshInterval = TimeSpan.FromSeconds(30);              // 0 turns polling off
    options.Optional = builder.Environment.IsDevelopment();           // start even if the platform is unreachable
    options.CacheDirectory = "/var/cache/invoicing/storyteller";      // the last-known-good copy on disk
});
```

The most useful knobs:

| Option | Variable | Default | |
| --- | --- | --- | --- |
| `Mode` | `STORYTELLER_MODE` | `resolved` | `resolved` evaluates the `@…` bindings; `effective` leaves them as written |
| `RefreshInterval` | `STORYTELLER_REFRESH_INTERVAL` | `30s` | How often to poll, with a little jitter so a fleet doesn't poll in lockstep |
| `Optional` | `STORYTELLER_OPTIONAL` | `false` | Start without the platform when it is **unreachable** and nothing is cached |
| `CacheDirectory` | `STORYTELLER_CACHE_DIR` | none | Keeps the last applied document on disk |
| `Placement` | – | `BeforeEnvironmentVariables` | `appsettings.json` < Storyteller < environment < command line. `Last` makes Storyteller win over everything |

## When things go wrong (and they will)

| What happens | What your app does |
| --- | --- |
| The platform is slow or down during a refresh | Keeps the document it has, logs a warning, tries again |
| It is down at startup, with a cache entry | Starts from the cache and reports itself degraded |
| It is down at startup, no cache, `Optional` | Starts empty and degraded, catches up later |
| It is down at startup, no cache, not optional | Fails to start. On purpose. |
| Wrong key, refused credential (`404`, `401`, `403`) | Fails to start, also when optional: that's a mistake to fix, not an outage to wait out |
| A new document that doesn't bind to your options | Rolled back; the previous document stays |

::: details Why doesn't `Optional` hide a wrong key?
Because "the platform is unreachable" will fix itself, and "you typed the key wrong" will not. `Optional` is about reachability only, so a typo can't hide behind it.
:::

::: warning A disk cache and secrets
A caller allowed to read secrets gets every resolved read with `Cache-Control: no-store`, so nothing lands on disk. To cache those documents anyway, give the provider an `IStorytellerCacheProtector` that encrypts the entries (for example over ASP.NET Core Data Protection). The [reference](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-provider.md#protection) has a ready-made one.
:::

## The refresher: health checks and buttons

`IStorytellerConfigurationRefresher` (in the container after `AddStorytellerConfiguration()`) tells you how things are, and lets you poke it:

```csharp
using _42.Platform.Storyteller.Sdk.Configuring;

app.MapGet("/health/config", (IStorytellerConfigurationRefresher refresher) =>
    refresher.Status.IsDegraded ? Results.Problem(refresher.Status.ToString()) : Results.Ok(refresher.Status.ActiveKey));

app.MapPost("/config/refresh", async (IStorytellerConfigurationRefresher refresher, CancellationToken cancellationToken) =>
    await refresher.TryRefreshAsync(cancellationToken) ? Results.Ok() : Results.StatusCode(503));
```

- `Status` says where the data came from (`Platform`, `Cache` or `Empty`), when it last succeeded, and how many times it failed in a row. It never contains a value.
- `TryRefreshAsync` reads now. It never throws for a failed read, it just says `false`.
- `SwitchKeyAsync("exe.contoso.invoicing.production")` swaps the whole configuration for another key of the same project, at runtime. Handy for a demo, a support tool, or a very indecisive service.

## Jobs: any document, on demand

A job that doesn't know its key at startup, or needs many of them, asks `IStorytellerDocuments`:

```csharp
using _42.Platform.Storyteller.Sdk.Configuring;

builder.Services.AddStorytellerDocuments();

public sealed class NightlyExport(IStorytellerDocuments documents)
{
    public async Task RunAsync(string customer, CancellationToken cancellationToken)
    {
        // An annotation key in the organization, project and view of STORYTELLER_KEY.
        var document = await documents.GetAsync($"uxe.{customer}.invoicing.production.nightly-export", cancellationToken);
        var settings = document.Get<ExportSettings>("export");
    }
}
```

Every call revalidates with an `ETag`, and concurrent calls for the same document share one request. No polling loop, no disk cache: a job decides for itself what to do without its configuration.

## Good to know

- **Keys are flattened** the `IConfiguration` way: `db:hosts:0`. Two names that differ only in case are refused, because `IConfiguration` couldn't tell them apart.
- **Numbers arrive as numbers**, so `1.50` becomes `1.5`. If the text matters, store it as a string.
- **Nothing secret is ever logged**, thrown or written into the status. Messages name keys and JSON paths, not values.
- **One key per source.** A worker that serves every customer wants [Hosting](hosting#a-worker-for-every-customer) instead.

## Reference

Every option, every failure class and the cache's file format: [Configuration Provider for .NET](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-provider.md) on GitHub. To watch it live, run `DotNet.SimpleConfiguration` of the [playground](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/playground.md).
