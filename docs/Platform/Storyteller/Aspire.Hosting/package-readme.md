# Storyteller Aspire hosting integration

Passes the `STORYTELLER_*` variables of the 2S Platform Storyteller from an Aspire AppHost to the projects, executables and containers it starts. A Storyteller resource holds the API's endpoint, the view of short keys and one machine credential; `WithReference` writes them into a process, and `WithStorytellerKey` or `WithExecution` its `STORYTELLER_KEY`. The processes read them with `42.Platform.Storyteller.Configuration`, `42.Platform.Storyteller.Hosting` or `sform`.

## Install

```powershell
dotnet add package 42.Platform.Storyteller.Aspire.Hosting --prerelease
```

## AppHost.cs

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var storyteller = builder.AddStoryteller("storyteller", "https://api.2s.42for.net/api")
    .WithView("house", "main")
    .WithApiKey(builder.AddParameter("storyteller-api-key", secret: true));

// STORYTELLER_ENDPOINT, STORYTELLER_API_KEY and STORYTELLER_KEY=@house.main.default.exe.northwind.invoicing.production
builder.AddProject<Projects.Invoicing_Api>("invoicing-api")
    .WithReference(storyteller)
    .WithExecution("northwind", "invoicing", "production");

builder.Build().Run();
```

- The endpoint is a URL, a parameter, or an expression such as `$"{api.GetEndpoint("http")}/api"` for the local platform's `Api.Functions` project.
- Secrets are parameters: `WithApiKey`, `WithClientCredentials`, and `WithClientCertificate` with its password. One credential per resource.
- In run mode the other `STORYTELLER_*` names are written empty, so a variable in the AppHost's environment does not reach the process.
- A container, and any process in publish mode, takes no client certificate and no `localhost` endpoint.
- The resource has no connection string, and is not in the manifest. It references `Aspire.Hosting` 13.1.2 only, not the SDK.

## Documentation

The resource, credentials, keys, the local platform and containers: [Aspire Hosting Integration](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/aspire.md).
