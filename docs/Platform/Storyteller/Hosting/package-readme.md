# Storyteller hosting for .NET

Reads who a process is in the 2S Platform Storyteller, and the subscriptions of a responsibility. `IExecutionContext` reads the annotation of `STORYTELLER_KEY` and its ancestors, and tells whether the subscription is in effect. `ISubscriptionCatalog` reads executions and streams the subscriptions of a responsibility in a context. `IStorytellerSubscriptions` keeps the resolved document and the named options of every subscription in memory, for a worker that serves many subjects. It builds on the configuration provider `42.Platform.Storyteller.Configuration`.

## Install

```powershell
dotnet add package 42.Platform.Storyteller.Hosting --prerelease
```

## Program.cs

A service of one subject:

```csharp
using _42.Platform.Storyteller.Sdk.Hosting;

var builder = WebApplication.CreateBuilder(args);

// The provider loads the configuration of STORYTELLER_KEY into IConfiguration; who am I, the catalog and the
// documents go into the container.
builder.AddStoryteller();

var app = builder.Build();

app.MapGet("/subscription", async (IExecutionContext self, CancellationToken cancellationToken) =>
{
    var context = await self.GetAsync(cancellationToken);
    return context.GetSubscriptionStatus(DateTimeOffset.UtcNow).ToString();
});

app.Run();
```

A worker for every subject of a responsibility, with `STORYTELLER_KEY=@house.main.default.rst.invoicing`:

```csharp
// IStorytellerSubscriptions, and IOptionsMonitor<InvoicingOptions>.Get(subject) bound to each subject's document.
builder.Services.AddStoryteller()
    .AddSubscriptions("production")
    .AddSubjectOptions<InvoicingOptions>("invoicing");
```

- `IStorytellerSubscriptions.WatchAsync` gives `Added` for every current subscription, then `Added`, `Changed` and `Removed` as they happen, each with the subject's execution, usage and document.
- The settings are the provider's: `STORYTELLER_ENDPOINT`, `STORYTELLER_KEY` and the variables of the machine's credential, or the same options in code.
- Who am I and the catalog need `Annotation.Read`; the subscriptions also need `Configuration.Read`, and `Secrets.Read` for secrets. The informer needs an unrestricted machine, or one restricted to the responsibility.
- The subscriptions poll, every 30 seconds by default, and keep the last good document of a subject when a read fails.
- Use it in the same version as `42.Platform.Storyteller.Configuration`.

## Documentation

Who am I, the catalog and the informer, the subscriptions and their named options, scopes, cost and limits: [Hosting for .NET](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/hosting.md).
