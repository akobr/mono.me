---
pageClass: dev-guide
---

# Aspire

**`42.Platform.Storyteller.Aspire.Hosting`** teaches a .NET Aspire AppHost about Storyteller. Declare the platform once, reference it from your projects, and every one of them starts with the right `STORYTELLER_*` variables.

> Stop copy-pasting environment variables between five terminals like it's 2009.

## Why you'll like it

- **Declare once, reference anywhere.** One resource holds the endpoint, the view and the machine credential.
- **Secrets as parameters.** The API key comes from an Aspire parameter, so it lives in user secrets and the dashboard masks it.
- **Keys that read like intent.** `WithExecution("northwind", "invoicing", "production")` says what the process is.
- **Tiny.** It references `Aspire.Hosting` and nothing else: no SDK dependencies sneak into your AppHost.

## Install

In the **AppHost** project only:

```bash
dotnet add package 42.Platform.Storyteller.Aspire.Hosting --prerelease
```

There is no client package. Your services read the variables with the [configuration provider](configuration) or [Hosting](hosting), exactly as they do outside Aspire.

## An AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// From the AppHost's configuration, Parameters:storyteller-api-key: its user secrets, for example.
var apiKey = builder.AddParameter("storyteller-api-key", secret: true);

var storyteller = builder.AddStoryteller("storyteller", "https://api.2s.42for.net/api")
    .WithView("house", "main")          // the view defaults to "default"
    .WithApiKey(apiKey);

// A service of one customer: STORYTELLER_KEY=@house.main.default.exe.northwind.invoicing.production
builder.AddProject<Projects.Invoicing_Api>("invoicing-api")
    .WithReference(storyteller)
    .WithExecution("northwind", "invoicing", "production");

// A worker for every customer: STORYTELLER_KEY=@house.main.default.rst.invoicing
builder.AddProject<Projects.Invoicing_Worker>("invoicing-worker")
    .WithReference(storyteller)
    .WithStorytellerKey("rst.invoicing");

builder.Build().Run();
```

```bash
dotnet user-secrets set Parameters:storyteller-api-key "2s.…"
```

No `using` needed: the types live in Aspire's own namespaces.

## Credentials

| Call | Gives the process |
| --- | --- |
| `WithApiKey(apiKey)` | `STORYTELLER_API_KEY` |
| `WithClientCredentials(clientId, clientSecret, tokenUrl, tokenScope)` | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET` and the token settings you set |
| `WithClientCertificate(path, password)` | `STORYTELLER_CLIENT_CERTIFICATE` and its password |

One resource, one machine. Processes that need different machines (one restricted to each execution, say) get one Storyteller resource each.

## Keys

| Process | Key |
| --- | --- |
| A service of one customer | `WithExecution("northwind", "invoicing", "production")` |
| A job | `WithStorytellerKey("uxe.northwind.invoicing.production.nightly-export")` |
| A worker for every customer | `WithStorytellerKey("rst.invoicing")` |
| Something in another project | `WithStorytellerKey("@house.billing.default.exe.northwind.invoicing.production")` |

A short key gets the `@organization.project.view.` of `WithView`. Keys are checked when you write them, so a typo fails the AppHost, not your service at 3 a.m.

## Good to know

- **Containers and deployments** take an API key or client credentials and an `https` endpoint. A client certificate and a `localhost` endpoint only make sense on the AppHost's own machine, so they're refused there, with a message that says so.
- **Against the local platform**, point the resource at the API project's endpoint: `builder.AddStoryteller("storyteller", $"{api.GetEndpoint("http")}/api")`, and `WaitFor(api)` in your projects.
- **No token files or federation.** Processes started by an AppHost have no CI token to federate with.
- **Leftover variables.** In run mode `WithReference` blanks the other credential names of the AppHost's environment, so a machine you exported for `sform` doesn't leak into your services.

## Reference

[Aspire Hosting Integration](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/aspire.md) on GitHub. `DotNet.AspireHost` of the [playground](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/playground.md) is a complete AppHost to copy from.
