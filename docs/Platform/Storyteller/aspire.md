# Aspire Hosting Integration

The package `42.Platform.Storyteller.Aspire.Hosting` lets an Aspire AppHost pass the `STORYTELLER_*` variables to the projects, executables and containers it starts. A Storyteller resource holds the API's endpoint, the view of short keys and one machine credential. `WithReference` writes its variables into a process, and `WithStorytellerKey` or `WithExecution` writes the process's `STORYTELLER_KEY`.

There is no client package. The process reads the variables as it does outside Aspire: with the [configuration provider](configuration-provider.md) (`builder.Configuration.AddStoryteller()`), with [Hosting](hosting.md) (`builder.AddStoryteller()`), with `sform`, or with any code that reads the same names.

The code is in `src/Platform/Storyteller/Aspire.Hosting/src/`. The design and its decisions are in [the spec](specs/2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md), section 10.

## Package

| Package | Namespace | Contents |
| --- | --- | --- |
| `42.Platform.Storyteller.Aspire.Hosting` | `Aspire.Hosting` | `AddStoryteller`, `WithView`, the credentials, `WithReference`, `WithStorytellerKey`, `WithExecution` |
| | `Aspire.Hosting.ApplicationModel` | `StorytellerResource` |

- **No `using`.** The types are in Aspire's own namespaces, as Aspire's integrations are, and an AppHost imports them already.
- **Aspire 13.1.2.** The package references `Aspire.Hosting` 13.1.2 and nothing else. It does not reference the C# SDK, so an AppHost does not depend on the SDK's version or pull in its dependencies. The variable names and the rules of keys and endpoints are copied from the SDK, and tests compare the copies with it.
- **Restore warnings.** Restoring `Aspire.Hosting` 13.1.2 reports the audit warnings `NU1902` and `NU1903` for its `MessagePack` 2.5.192 dependency, as the platform's own AppHost does.

## An AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// The value comes from the AppHost's configuration, Parameters:storyteller-api-key, for example its user secrets.
var apiKey = builder.AddParameter("storyteller-api-key", secret: true);

var storyteller = builder.AddStoryteller("storyteller", "https://api.2s.42for.net/api")
    .WithView("house", "main")
    .WithApiKey(apiKey);

// A service of one subject: STORYTELLER_KEY=@house.main.default.exe.northwind.invoicing.production
builder.AddProject<Projects.Invoicing_Api>("invoicing-api")
    .WithReference(storyteller)
    .WithExecution("northwind", "invoicing", "production");

// A worker for every subject of the responsibility: STORYTELLER_KEY=@house.main.default.rst.invoicing
builder.AddProject<Projects.Invoicing_Worker>("invoicing-worker")
    .WithReference(storyteller)
    .WithStorytellerKey("rst.invoicing");

builder.Build().Run();
```

The environment of `invoicing-api`:

```text
STORYTELLER_ENDPOINT=https://api.2s.42for.net/api
STORYTELLER_API_KEY=2s.…                 the value of the parameter, masked in the dashboard
STORYTELLER_KEY=@house.main.default.exe.northwind.invoicing.production
```

In run mode the other `STORYTELLER_*` names are there too, empty (see [`WithReference`](#withreference)).

## The resource

`AddStoryteller(name, endpoint)` adds a `StorytellerResource`. The endpoint is the API base URL, `/api` included.

| `endpoint` | Use |
| --- | --- |
| `string` | A literal URL |
| `IResourceBuilder<ParameterResource>` | A URL from a parameter, resolved at start; a manifest holds its reference |
| `ReferenceExpression` | Any expression |
| An interpolated string | `$"{api.GetEndpoint("http")}/api"`: the endpoint of a project of the AppHost, such as the [local platform](#the-local-platform) |

- **The URL rule.** A literal URL is checked at the call with the SDK's rule: absolute, `https`, or `http` only to `localhost` or a loopback address literal; no user info, query or fragment. Otherwise `AddStoryteller` throws `ArgumentException`, which names the parameter and not the value. Any endpoint without parameters or endpoint references counts as literal, whichever overload built it. The process checks the URL again when it reads it.
- **The interpolated string** binds to a handler overload, so its endpoint references stay references and are resolved at start.
- **Members.** `Endpoint` is the expression. `Organization`, `Project` and `View` are those of `WithView`, `null` until it is called.
- **Not a process.** The resource starts `Running`, with the resource type `Storyteller` and the endpoint as its source in the dashboard: the URL, or the text of the expression for a parameter or an endpoint reference, never a resolved parameter. `WaitFor(storyteller)` proceeds at once; to start after the local API, wait for its project.
- **Not in the manifest.** The resource is excluded from the manifest. In publish mode, the variables of the processes hold the endpoint and the parameters as expressions.
- **No connection string.** Nothing reads a `ConnectionStrings__storyteller`, so the resource has none. Aspire's own `WithReference(resource, "name")` therefore does not compile with it, instead of binding silently and passing no `STORYTELLER_*`.

`WithView(organization, project, view)` sets the view that prefixes the annotation keys of [`WithStorytellerKey`](#keys) and `WithExecution`. `view` is `default` when omitted. Each name is one segment of a key: an empty name, or one with a `.`, throws `ArgumentException`.

## Credentials

A resource holds one machine credential, as the SDK reads one from the variables.

| Call | Variables |
| --- | --- |
| `WithApiKey(apiKey)` | `STORYTELLER_API_KEY` |
| `WithClientCredentials(clientId, clientSecret, tokenUrl, tokenScope)` | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, and `STORYTELLER_TOKEN_URL` and `STORYTELLER_TOKEN_SCOPE` when they are set |
| `WithClientCertificate(path, password)` | `STORYTELLER_CLIENT_CERTIFICATE`, and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD` with a password |

```csharp
var storyteller = builder.AddStoryteller("storyteller", builder.AddParameter("storyteller-endpoint"))
    .WithClientCredentials(
        builder.AddParameter("storyteller-client-id"),
        builder.AddParameter("storyteller-client-secret", secret: true),
        tokenUrl: "https://auth.example.com/oauth2/token");
```

- **Secrets are parameters.** The API key, the client id and secret, and the certificate's password are parameters, so a secret never sits in the AppHost's code. Aspire resolves them at start and the dashboard masks the secret ones; in publish mode they are references. The token URL and scope are plain strings: leave them `null` to keep them unset, as an empty string throws `ArgumentException`.
- **One kind.** A resource has an API key, client credentials, or a client certificate, or an API key with a client certificate (the SDK's `CertificateAndApiKey`), in either order. Any other combination throws `InvalidOperationException` at the call that adds the second credential.
- **The certificate path** is made absolute against the AppHost directory when it is added, so every process finds the same file whatever its working directory. The file stays on the AppHost's machine: see [Containers and publish mode](#containers-and-publish-mode).
- **Not offered:** a token file and federation. The processes that an AppHost starts have no projected or CI token.
- **Without a credential**, a process gets only the endpoint, and its requests are anonymous: the API answers `401`.
- **One machine per resource.** Processes that need different machines, for example one restricted to each execution, use one Storyteller resource each.

```csharp
var northwind = builder.AddStoryteller("storyteller-northwind", "https://api.2s.42for.net/api")
    .WithView("house", "main")
    .WithApiKey(builder.AddParameter("storyteller-northwind-api-key", secret: true));
```

## `WithReference`

`WithReference(storyteller)` gives a project, an executable or a container, any `IResourceWithEnvironment`, the variables of the resource.

- **What it writes.** `STORYTELLER_ENDPOINT`, and the variables of the credential that are set (see [Credentials](#credentials)). Parameters and expressions stay what they are, so Aspire resolves them at start, and a manifest holds their references.
- **Masking in run mode.** DCP starts a project or an executable with the AppHost's environment, so a `STORYTELLER_*` left there, for `sform` say, would mix with the referenced machine. In run mode `WithReference` therefore writes every other name the SDK reads empty: `STORYTELLER_API_URL`, `STORYTELLER_KEY`, and every credential name, the deprecated ones and those not offered included. The C# SDK, and with it `sform`, reads an empty variable as unset. A value that another callback wrote, such as the key of `WithStorytellerKey` or a `WithEnvironment` of the application, is kept. A container, and any process in publish mode, inherits nothing, so nothing is masked there.
- **The dashboard** draws the reference as a relationship.
- **One reference.** A second `WithReference` to a Storyteller resource replaces the first: a process has one set of `STORYTELLER_*`.
- **A process without a key.** `STORYTELLER_KEY` is masked too, so a process that reads its key from the AppHost's environment gets an empty one. Set the key with `WithStorytellerKey` or `WithExecution`.

### Containers and publish mode

A client certificate and a loopback endpoint exist only on the AppHost's machine.

| Destination | Refused, with `InvalidOperationException` |
| --- | --- |
| A container | A resource with a client certificate; a literal endpoint to `localhost` or a loopback address, which the container takes for itself |
| Any process, in publish mode | The same: a deployed process runs elsewhere |

- **When.** At `WithReference`, or, for a certificate added after it, from the environment callback, so Aspire fails the start or the publish of that resource. The messages name the resources, never the values.
- **A container** takes an API key or client credentials, and an `https` endpoint it reaches, for example from a parameter. The SDK accepts `http` only to a loopback address, so an `http` endpoint under another host name, such as an endpoint reference that a container reaches through the host, is refused by the SDK inside the container.
- **A certificate for local runs only.** Add it in run mode, and give a deployed process an API key or client credentials:

```csharp
if (builder.ExecutionContext.IsRunMode)
{
    storyteller.WithClientCertificate("certificates/machine.pfx", builder.AddParameter("storyteller-certificate-password", secret: true));
}
else
{
    storyteller.WithApiKey(builder.AddParameter("storyteller-api-key", secret: true));
}
```

## Keys

`WithStorytellerKey(key)` writes `STORYTELLER_KEY`:

- **A full key**, `@{organization}.{project}.{view}.{annotation key}`, is used as written. It needs no `WithReference`.
- **An annotation key** is prefixed with `@{organization}.{project}.{view}.` of the `WithView` of the Storyteller resource that the process references. The prefix is added at start, so the order of `WithStorytellerKey`, `WithReference` and `WithView` does not matter.
- **Checked at the call** with the rules of a full key: a known type code (`rst`, `sbt`, `usg`, `cnt`, `unt`, `exe`, `uxe`, in any case), the segment count of its type, and no empty segment. An annotation key is checked as the full key it becomes. An invalid key throws `ArgumentException`.
- **The last call wins**, of `WithStorytellerKey` and `WithExecution`. An earlier key is not used, and not checked at start.
- **Without a view.** An annotation key of a process that references no Storyteller resource, or one without `WithView`, throws `InvalidOperationException` at start, naming `WithView` and `WithReference`, and Aspire fails the start of that process.

`WithExecution(subject, responsibility, context)` is `WithStorytellerKey($"exe.{subject}.{responsibility}.{context}")`. Each name is one segment: an empty name, or one with a `.`, throws `ArgumentException`.

| Process | Key |
| --- | --- |
| A service of one subject | `WithExecution("northwind", "invoicing", "production")` |
| A job | `WithStorytellerKey("uxe.northwind.invoicing.production.nightly-export")` |
| A worker for every subject of a responsibility | `WithStorytellerKey("rst.invoicing")` |
| A process in another project | `WithStorytellerKey("@house.billing.default.exe.northwind.invoicing.production")` |

A machine belongs to one project, so a full key in another project than the referenced resource's view needs a machine of that project.

## The local platform

`src/Platform/Aspire.Host` runs the platform locally, with its `Api.Functions` project. An AppHost that also runs a service against it points the Storyteller resource at that project's endpoint:

```csharp
var api = builder.AddProject<Projects.Api_Functions>("api-functions");   // with its Cosmos DB, storage and Key Vault, as in src/Platform/Aspire.Host

var storyteller = builder.AddStoryteller("storyteller", $"{api.GetEndpoint("http")}/api")
    .WithView("house", "main")
    .WithApiKey(builder.AddParameter("storyteller-api-key", secret: true));

builder.AddProject<Projects.Invoicing_Worker>("invoicing-worker")
    .WithReference(storyteller)
    .WithStorytellerKey("rst.invoicing")
    .WaitFor(api);
```

- **The endpoint** is an endpoint reference, resolved at start to the port Aspire allocated. `http` to `localhost` is accepted for projects and executables.
- **The order of starts.** `WaitFor(storyteller)` proceeds at once, so the process waits for the API project.
- **The machine.** Create a machine in the local platform with `sform account machine create`, and put its API key into the parameter, for example with `dotnet user-secrets set Parameters:storyteller-api-key <key>` in the AppHost's project.
- **Not wired.** `src/Platform/Aspire.Host` itself has no project that reads Storyteller, so it does not use this integration.

## What other processes get

The variables are the shared integration contract of the C# SDK and `sform` (see [Credentials](configuration-provider.md#credentials)). They reach every destination the same way:

| Destination | Reads them with |
| --- | --- |
| A .NET project | The configuration provider, Hosting, or the C# SDK's `StorytellerCredential.FromEnvironment()` |
| `sform`, started as an executable | The same contract, through the C# SDK |
| A Node.js, Python or Java executable or container | Its own code: the TypeScript SDK reads no environment variables |

## Limits

- **No client package.** There are no Aspire health checks, tracing or `Aspire:*` settings for Storyteller. The process's "one call" is `builder.AddStoryteller()` of [Hosting](hosting.md#one-call).
- **No token file or federation** credentials.
- **One machine per resource**, and one Storyteller resource per process.
- **Containers and publish mode** take no client certificate and no literal loopback endpoint.
