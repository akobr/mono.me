# Hosting

**`42.Platform.Storyteller.Hosting`** answers the big questions a process asks itself: *who am I*, *who do I work for*, and *what changed since I last looked*. It brings the [configuration provider](configuration) along, so one set of variables drives both.

> Philosophy for services, in a NuGet package.

## Why you'll like it

- **Who am I, in one call.** Your execution, its customer, its module, its environment and whether its subscription is still active, as typed snapshots.
- **One worker, every customer.** A worker that serves a module for everybody learns who subscribed, who left, and keeps each customer's document in memory, with options bound **per customer**.
- **The whole catalog, typed.** Read any annotation, list what hangs under it, or watch it change.
- **Polling done right.** `ETag`s, jitter, retries and a circuit breaker are in the box. Your code just `await foreach`es.

## Install

```bash
dotnet add package 42.Platform.Storyteller.Hosting --prerelease
```

It brings `42.Platform.Storyteller.Configuration` with it. Let it: don't add or update `Configuration` on its own to another version.

## A service of one customer

```csharp
using _42.Platform.Storyteller.Sdk.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddStoryteller();   // the provider and Hosting in one go: IConfiguration + who am I + the catalog

var app = builder.Build();

app.MapGet("/whoami", async (IExecutionContext self, CancellationToken cancellationToken) =>
{
    var context = await self.GetAsync(cancellationToken);

    return new
    {
        Customer = context.Subject?.Title,
        Module = context.Responsibility?.Name,
        Environment = context.Context?.Name,
        Subscription = context.GetSubscriptionStatus(DateTimeOffset.UtcNow).ToString(),   // Active, Expired, …
    };
});

app.Run();
```

`IExecutionContext` reads the annotation of `STORYTELLER_KEY` and its ancestors once, then serves the snapshot instantly and refreshes it in the background every 5 minutes. The usage's `Values` (`context.Usage?.Values`) are where plans and seats live.

::: info A machine restricted to its execution can't see its parents
That's the point of restricting it. Its ancestors then show up in `context.Unreadable`, and the subscription status is `Unknown` instead of `Active`. Need the full picture? Restrict the machine to the responsibility instead.
:::

## A worker for every customer

One process, one module, all customers. Its key is the responsibility, and it subscribes to one context:

```csharp
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// STORYTELLER_KEY=@house.main.default.rst.invoicing
builder.Services.AddStoryteller()
    .AddSubscriptions("production", options => options.WaitUntilReadyOnStart = true)
    .AddSubjectOptions<InvoicingOptions>("invoicing");

builder.Services.AddHostedService<InvoicingWorker>();

builder.Build().Run();

public sealed class InvoicingWorker(IStorytellerSubscriptions subscriptions, IOptionsMonitor<InvoicingOptions> options)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Added for every current customer first, then the changes as they happen.
        await foreach (var change in subscriptions.WatchAsync(stoppingToken))
        {
            var customer = change.Subscription.Subject;

            if (change.Kind == StorytellerChangeKind.Removed)
            {
                StopTenant(customer);
                continue;
            }

            try
            {
                RunTenant(customer, options.Get(customer));   // options bound to this customer's document
            }
            catch (Exception exception) when (exception is OptionsValidationException or StorytellerConfigurationException)
            {
                // One customer's broken document must not stop everybody else.
            }
        }
    }

    private void RunTenant(string customer, InvoicingOptions options) { /* start, or apply new options */ }

    private void StopTenant(string customer) { /* stop */ }
}
```

- A **subscription** is an execution of the responsibility in that context, whose execution and usage are both active. Disable a usage, let it expire, delete it: the worker gets `Removed`, with the reason.
- `IOptionsMonitor<T>.Get("northwind")` binds Northwind's resolved document. `ConfigureAll<T>` gives defaults that every customer shares.
- `WaitUntilReadyOnStart` holds the host's start until the first poll and the first documents are in.
- `subscriptions.Status` has `IsReady` and `IsDegraded`, made for a health check.

::: tip Catch per customer
An exception that escapes `ExecuteAsync` stops the host, and with it **every** customer. Catch the failures of one customer inside the loop, as above.
:::

## The catalog: anything, anywhere

`IStorytellerCatalog` reads any annotation, lists what hangs under one, and watches it:

```csharp
using _42.Platform.Storyteller.Sdk.Hosting;

// One annotation, typed.
StorytellerUnit? export = await catalog.GetAsync<StorytellerUnit>("unt.invoicing.nightly-export", cancellationToken);

// The units of a responsibility.
var units = await catalog.ListAsync(StorytellerTarget.UnitsOf("rst.invoicing"), cancellationToken);

// Northwind's executions in production, as they change.
var target = StorytellerTarget.ExecutionsOf("sbt.northwind").WithContext("production");

await foreach (var change in catalog.WatchAsync(target, TimeSpan.FromMinutes(1), cancellationToken))
{
    Console.WriteLine($"{change.Kind} {change.Annotation.AnnotationKey}");
}
```

| Target | Lists | Under |
| --- | --- | --- |
| `UsagesOf(key)` | usages | a responsibility or a subject |
| `ContextsOf(key)` | contexts | a subject |
| `UnitsOf(key)` | units | a responsibility |
| `ExecutionsOf(key)` | executions | a responsibility, subject, usage or context |
| `UnitsOfExecutionOf(key)` | units of execution | almost anything above them |

`ISubscriptionCatalog` is the lightweight cousin of the worker: `GetExecutionAsync(key)` reads one execution, and `WatchExecutionsAsync("invoicing", "production")` streams the subscriptions without keeping their documents.

## Which one do I use?

| You want | Use |
| --- | --- |
| Your own configuration | `builder.AddStoryteller()` and `IOptionsMonitor<T>` |
| To know who you are and if you're still subscribed | `IExecutionContext` |
| To serve every customer of a module | `AddSubscriptions` + `AddSubjectOptions<T>` + `IStorytellerSubscriptions` |
| To follow any part of the catalog in a tool | `IStorytellerCatalog` |
| A document now, in a job | `IStorytellerDocuments` of the [provider](configuration#jobs-any-document-on-demand) |

## The machine each one needs

| Process | Machine |
| --- | --- |
| A service of one customer | `sform account machine create -a exe.northwind.invoicing.production` |
| A worker for every customer | `sform account machine create -a rst.invoicing` |
| …that needs secrets in the documents | add `-r --scope secrets-read` |

The default scopes (`Annotation.Read` and `Configuration.Read`) cover who am I, the catalog and the documents.

## Good to know

- **It polls.** A change reaches you at the next poll, 30 seconds by default for the subscriptions.
- **Feature flags** come along for free when they're in the document: Microsoft.FeatureManagement reads its `feature_management` section from `IConfiguration`.
- **Test doubles are easy.** The snapshots have public constructors and `init` members, so a fake `IExecutionContext` is a few lines.

## Reference

[Hosting for .NET](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/hosting.md) on GitHub: snapshots, targets, the informer's every rule, costs and limits. `DotNet.SimpleExecutor` of the [playground](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/playground.md) runs who am I and both catalogs.
