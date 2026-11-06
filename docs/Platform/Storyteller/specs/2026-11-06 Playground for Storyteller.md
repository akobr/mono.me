# Playground for Storyteller

Linear: [424-205](https://linear.app/424fun/issue/424-205/create-a-playground-for-storyteller).

## Problem

The .NET packages of Storyteller have reference documentation and unit tests, but nothing in the repository runs them together. There is no process to start that shows a configuration arriving in `IConfiguration`, an `IOptionsMonitor<T>` following a change on the platform, or the catalog listing executions and their units. The Aspire integration has no AppHost that uses it: `aspire.md` says that `src/Platform/Aspire.Host` "has no project that reads Storyteller".

So a developer who wants to try a change of the provider, of Hosting or of the integration writes a throwaway application first. The packages also have no small example to copy.

## Current State

- **`42.Platform.Storyteller.Configuration`** (`src/Libraries/Platform/Storyteller/Configuration/src/`). `builder.Configuration.AddStoryteller()` loads the configuration of `STORYTELLER_KEY` into `IConfiguration`, and polls it. `StorytellerConfigurationProvider` is public. `services.AddStorytellerConfiguration()` adds the provider's logging and `IStorytellerConfigurationRefresher`. A first read that fails throws out of `AddStoryteller`, except an unreachable platform under `STORYTELLER_OPTIONAL`.
- **`42.Platform.Storyteller.Hosting`** (`src/Libraries/Platform/Storyteller/Hosting/src/`). `services.AddStoryteller()` registers `IExecutionContext`, `ISubscriptionCatalog` and `IStorytellerDocuments`, and reads and validates its settings at the call.
  - `ISubscriptionCatalog` has `GetExecutionAsync(key)` and `WatchExecutionsAsync(responsibility, context, pollInterval)`. `TimeSpan.Zero` polls once and throws the failure of that poll; any other interval logs a failed poll and goes on.
  - It has no read of units or units of execution. `StorytellerExecution.UnitNames` names the units of execution under an execution. A `StorytellerAnnotation` of a responsibility does not carry the `UnitNames` of the model.
  - `IExecutionContext` reads the annotation of any key, with the ancestors of its type.
  - `StorytellerDocument` has an internal constructor, so a test cannot fake `IStorytellerDocuments`.
- **`42.Platform.Storyteller.ConfigurationFormats`** (`src/Libraries/Platform/Storyteller/ConfigurationFormats/src/`). `ConfigurationFormatter.Format(content, format)` renders a document as `flat`, `properties`, `env` or `yaml`. `ConfigurationFormatNames.TryParse` reads the names. A document that a format cannot write throws `InvalidDocumentException`.
- **`42.Platform.Storyteller.Aspire.Hosting`** (`src/Libraries/Platform/Storyteller/Aspire.Hosting/src/`). `AddStoryteller`, `WithView`, `WithApiKey`, `WithReference`, `WithStorytellerKey` and `WithExecution` write the `STORYTELLER_*` variables of a process.
- **Other playgrounds and examples.** `src/Crumble/Playground/` is a set of projects with an AppHost. `src/Platform/Examples/CustomClientApp/` is one small client without tests.
- **The local platform.** `src/Platform/Aspire.Host` runs the API on the fixed port 7071, over IPv4. DbCreator, started from its dashboard, seeds the organization `42dotnet` with the project `main` (see `docs/Platform/development.md`). Its launch profiles use the ports 17294, 15075, 21038, 23035, 22221, 19191, 18226 and 20053.
- **`sform story set` cannot write today.** `ToSdkAnnotation` (`src/Platform/Cli/src/Model/AnnotationExtensions.cs`) does not set `AnnotationKey`, which the SDK's model requires, so serializing the body throws. `sform story import` works over another route, and creates missing ancestors. This bug is out of scope here; the review reports it.
- **Wiring.** `src/Platform/Directory.Build.proj` builds every project below it, so new projects join the traversal by themselves. A project joins `dotnet test --solution` through `42.mono.slnx`.

## Proposed Changes

### 1. Layout

Everything goes to `src/Platform/Storyteller/Playground/`. Each project follows the repository's `<project>/src/` and `<project>/test/` layout:

```text
src/Platform/Storyteller/Playground/
├── data/                       the data of the playground, for sform story import
├── DotNet.SimpleConfiguration/
│   ├── src/DotNet.SimpleConfiguration.csproj
│   └── test/DotNet.SimpleConfiguration.UnitTests.csproj
├── DotNet.SimpleExecutor/
│   ├── src/DotNet.SimpleExecutor.csproj
│   └── test/DotNet.SimpleExecutor.UnitTests.csproj
└── DotNet.AspireHost/
    └── src/DotNet.AspireHost.csproj
```

- **Names.** Assemblies are `42.Platform.Storyteller.Playground.DotNet.<Name>`, root namespaces `_42.Platform.Storyteller.Playground.DotNet.<Name>`. The file names of the projects give `Projects.DotNet_SimpleConfiguration` and `Projects.DotNet_SimpleExecutor` in the AppHost.
- **Not packed.** Each project sets `<IsPackable>false</IsPackable>`.
- **Solution.** A new folder `/Platform/Storyteller/Playground/` in `42.mono.slnx` lists the five projects.
- **Project references.** The applications reference the libraries as projects, so the playground always runs the code of its commit.

### 2. `DotNet.SimpleConfiguration`

The smallest application that receives the configuration of a responsibility as `IConfiguration`, with a console renderer that shows every change that reaches `IOptionsMonitor<T>`.

```csharp
var builder = Host.CreateApplicationBuilder(args);

try
{
    // STORYTELLER_ENDPOINT, STORYTELLER_KEY (@{organization}.{project}.{view}.rst.{responsibility}) and the machine's credential.
    builder.Configuration.AddStoryteller();
}
catch (StorytellerConfigurationException exception)
{
    // The message names what to fix, and never a value.
    Console.Error.WriteLine(exception.Message);
    return 1;
}

builder.Services.AddStorytellerConfiguration();

// The usual binding: IOptionsMonitor<PlaygroundOptions> follows every document the provider applies.
builder.Services.Configure<PlaygroundOptions>(builder.Configuration);

builder.Services.AddSingleton<TextWriter>(Console.Out);
builder.Services.AddHostedService<OptionsRenderer>();

await builder.Build().RunAsync();
return 0;
```

**`PlaygroundOptions`** is a small typed class: `Greeting`, `BatchSize` and `Tags`. It is what an application would copy, and a value that does not convert shows the provider's rollback, which only `ConfigurationBinder` can trigger.

**`OptionsRenderer`** is an `IHostedService` over `IOptionsMonitor<PlaygroundOptions>`, `IConfiguration`, the refresher and a `TextWriter`.

- At start it writes the active key (`IStorytellerConfigurationRefresher.ActiveKey`), every key of the document, and the options.
- On each `OnChange` it writes the time, one line per key that changed, and the options. A notification that changed no key says so, so every notification stays visible.
- The options are only part of a document, and a key that they do not name would show nothing. So the lines come from the keys of the document, and adding any key on the platform shows at once.

```text
@42dotnet.main.default.rst.playground
  batchSize = "10"
  greeting = "Hello"
  tags:0 = "alpha"
Options: {"Greeting":"Hello","BatchSize":10,"Tags":["alpha"]}
12:03:21 IOptionsMonitor changed
  ~ batchSize = "10" -> "20"
  + tags:1 = "beta"
  - greeting = "Hello"
Options: {"Greeting":null,"BatchSize":20,"Tags":["alpha","beta"]}
```

**`DocumentKeys.Read(IConfiguration)`** gives the keys of the document as a dictionary without case, as `IConfiguration` compares keys.

- It walks the `StorytellerConfigurationProvider`s in `IConfigurationRoot.Providers` with `GetChildKeys`, which returns one segment per leaf, so segments are de-duplicated without case. `TryGet` tells a value from a section.
- It reads each value through `IConfiguration`. So environment variables and `appsettings.json` add no keys, and a value that an environment variable overrides shows as the application reads it.
- It does not sort with `ConfigurationKeyComparer`: that comparer treats `a:01` and `a:1` as equal, and these are two keys of a valid document. The order is the order of the walk.

**`ConfigurationDiff.Compare(before, after)`** is a pure function: `Added`, `Changed` and `Removed` per key. Keys compare without case; values compare ordinally.

**Rules of the callback.**

- A lock keeps the first render and the callbacks apart.
- The callback must not throw on its own: the provider counts a throwing change callback as a document that does not bind, and rolls the document back. Walking the keys and writing to the console do not throw.
- Values are written with `JsonConvert.ToString`, so a line end or a C0 control character in a value cannot break a line.

**What a failure looks like.**

- **A first read that fails** ends the application with the message and exit code `1`: a wrong key, a refused credential, missing settings. Only an unreachable platform can start under `STORYTELLER_OPTIONAL`, empty, and then it shows the document as added keys when the platform answers.
- **A later read that fails** keeps the applied document. The provider logs a warning, which the host's console logger prints.
- **`batchSize` set to `abc`:** the rebind fails, so the callback is not raised for it. The provider logs the rejection and restores the previous document, which raises the callback with no changed key.
- **A document that does not bind at the first start** throws the binder's exception out of the renderer's first read, and the host does not start. The binder's message names the key and the value.

### 3. `DotNet.SimpleExecutor`

A console application over `IExecutionContext` and `ISubscriptionCatalog`. It renders who it is, the executions in its scope, and their units and units of execution, with the configuration of each written by `ConfigurationFormatter`.

```text
DotNet.SimpleExecutor [all|who|executions|units|units-of-execution]
                      [-c|--context <name>] [-f|--format flat|properties|env|yaml] [-w|--watch]
```

| Argument or option | Default | Meaning |
| --- | --- | --- |
| The first argument | `all` | What to render |
| `--context` | | The context to list. Required for a key without one, refused for a key with one. One key segment |
| `--format` | `yaml` | The format of the documents, parsed with `ConfigurationFormatNames.TryParse` |
| `--watch` | Off | Keep following the informer, with a poll every 10 s. Without it, one read and the end |

**CLI pattern.** McMaster with the generic host, as the other CLIs: `ExecutorCommand : BaseCommand` of `Libraries/CLI/Toolkit`, started with `RunCommandLineApplicationAsync<ExecutorCommand>(args)`.

- `Program.RunAsync(args, console, addStoryteller)` holds the host, so the tests run it as `Main` does. It catches `CommandParsingException` and `StorytellerConfigurationException` of the registration, writes the message and returns `2`. The registration reads the settings, so `--help` needs them too; its message names what is missing.
- `new HostBuilder()`, as `sform` starts, so `--context` does not also reach `IConfiguration` as a command line setting.
- Logs at `Warning` and above, on standard error, so standard output holds only what the command renders.
- The output is written to `IExtendedConsole.Console.Out`, a plain `TextWriter`. The Toolkit's `Write` methods render through Alba, which wraps lines at the console's width, and a format's text must stay as it is.
- `IHostApplicationLifetime.ApplicationStopping` ends a watch on Ctrl+C, as `BaseCommand.OnExecuteAsync` takes no token.
- The first argument and the format are checked by hand: McMaster's validation attributes exit with `1`, not `2`.

**The scope** comes from the process's key, parsed with `FullKey.Parse(context.Key).Annotation`:

| Key | Context | Executions | Machine it needs |
| --- | --- | --- | --- |
| `rst.R` | `--context` | `WatchExecutionsAsync(R, C, TimeSpan.Zero)`, one poll; with `--watch`, every 10 s | Restricted to `rst.R`, or unrestricted |
| `usg.S.R` | `--context` | The same, keeping the events of `S` | The same |
| `unt.R.U` | `--context` | The same. Its own document needs no context, so `units` and `all` render it first, and without `--context` they end there | The same; its own document needs only its key |
| `exe.S.R.C`, `uxe.S.R.C.U` | From the key | `GetExecutionAsync("exe.S.R.C")`, in any status; with `--watch`, the informer, keeping the events of `S` | Restricted to the execution or above. `--watch` needs the machine of `rst.R` |
| `sbt.S`, `cnt.S.C` | | None: `who` and `all` render who, then anything else ends with a message and `2` | |

**What it renders:**

- **Who** (`IExecutionContext.GetAsync`): the key, the type, the title, `GetSubscriptionStatus(now)`, the ancestors that were read, and the unreadable ones.
- **An execution:** the event kind, the annotation key and its status, the usage's status and `Values` when the usage was read, its `UnitNames`, and its document. A `Removed` event writes its `Removal` and no document.
- **A unit** `unt.R.U`: its document, once per run, as several subjects share a unit.
- **A unit of execution** `uxe.S.R.C.U`: its document.

Each document is written under a line with its key, version and format, then the exact text of the format. Documents are effective, so they hold what they inherit. With the data of section 6:

```text
Who am I: @42dotnet.main.default.rst.playground
  Type: Responsibility
  Status: Active
Added exe.northwind.playground.production: Active
  Usage usg.northwind.playground: Active, plan=gold
  Units: nightly-export
--- exe.northwind.playground.production, version 1, yaml
batchSize: 50
greeting: "Hello"
tags:
  - "alpha"
--- unt.playground.nightly-export, version 1, yaml
batchSize: 10
greeting: "Hello"
schedule: "0 2 * * *"
tags:
  - "alpha"
--- uxe.northwind.playground.production.nightly-export, version 1, yaml
batchSize: 50
greeting: "Hello"
schedule: "0 2 * * *"
tags:
  - "alpha"
target: "northwind-archive"
```

**The units come from the executions.** The catalog lists no units, so a unit without a unit of execution under a listed execution does not show. Reading `Responsibility.UnitNames` needs a change of Hosting, which the review proposes as a follow-up.

**Failures.**

| Case | Result |
| --- | --- |
| Who am I fails, or the one poll fails (a responsibility that does not exist, a machine that may not list) | The message on standard error, exit code `1` |
| `GetExecutionAsync` finds no execution | The same |
| A document fails (`StorytellerConfigurationException`, or `InvalidDocumentException` of a format) | `! {key}: {message}`, the run goes on, exit code `1` at the end. Messages name keys and paths, never values |
| One execution or usage cannot be read in the one poll | The informer drops it and logs a warning, so it does not show, and the exit code stays `0` |
| Under `--watch`, a poll fails | The informer logs a warning on standard error and polls again, for ever |
| A cancelled watch | Exit code `0`. The host does not wait for the command, so anything after the cancellation may not be written |
| An unknown option, what to render, a format or a context the command refuses | The message on standard error, exit code `2` |

**What `--watch` sees** is what the informer sees: `Added`, `Changed` and `Removed` of the active execution and usage of each subject (see [One poll](../hosting.md#one-poll)).

- A configuration write does not move `Modified`, so it raises no event. `DotNet.SimpleConfiguration` is the project that follows a document.
- A new unit of execution raises no event, and later events keep the old `UnitNames`, until a restart.
- A disabled execution leaves the list, as `Removed`. Without `--watch`, an `exe` key still renders it, with its status.

### 4. `DotNet.AspireHost`

An AppHost on `Aspire.AppHost.Sdk` 13.6.4 that references `42.Platform.Storyteller.Aspire.Hosting` with `IsAspireProjectResource="false"`, and the two applications as project resources. Every `STORYTELLER_*` variable comes from the integration, except the one the application chooses for itself.

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var playground = builder.Configuration.GetSection("Playground");

var storyteller = builder.AddStoryteller("storyteller", builder.AddParameter("storyteller-endpoint"))
    .WithView(playground["Organization"]!, playground["Project"]!, playground["View"]!)
    .WithApiKey(builder.AddParameter("storyteller-api-key", secret: true));

builder.AddProject<Projects.DotNet_SimpleConfiguration>("simple-configuration")
    .WithReference(storyteller)
    .WithStorytellerKey($"rst.{responsibility}")
    // Starts without the platform while it is unreachable, and shows the document as added keys when it answers.
    .WithEnvironment("STORYTELLER_OPTIONAL", "true");

builder.AddProject<Projects.DotNet_SimpleExecutor>("simple-executor")
    .WithReference(storyteller)
    .WithStorytellerKey($"rst.{responsibility}")
    .WithArgs("--context", context, "--watch");

// One subject's point of view, started from the dashboard: WithExecution, and who am I with ancestors.
builder.AddProject<Projects.DotNet_SimpleExecutor>("simple-executor-of-subject")
    .WithReference(storyteller)
    .WithExecution(subject, responsibility, context)
    .WithExplicitStart();

builder.Build().Run();
```

- **`appsettings.json`** holds `Parameters:storyteller-endpoint` (`http://127.0.0.1:7071/api`, the local platform over IPv4) and the section `Playground`: `Organization` `42dotnet`, `Project` `main`, `View` `default`, `Responsibility` `playground`, `Subject` `northwind`, `Context` `production`.
- **The API key** is a secret parameter in the user secrets of the AppHost (`UserSecretsId`). Without it, the dashboard asks for it.
- **The local platform stays apart.** `src/Platform/Aspire.Host` runs it, so the same AppHost works against a deployed API by changing the parameter and the section. Start the platform first: `simple-executor` ends when who am I fails, and restarts from the dashboard.
- **The project file** follows `Aspire.Host.Platform.csproj`, with `NoWarn` of `ASPIRE010`.
- **Launch profiles.** `Properties/launchSettings.json` has an `https` and an `http` profile on ports other than those of `src/Platform/Aspire.Host`, so both hosts run at once. Both set `DOTNET_ENVIRONMENT=Development`, without which the AppHost reads no user secrets, and the dashboard's OTLP and resource service URLs. The `http` profile sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`.

### 5. Tests

Two xUnit v3 projects on Microsoft.Testing.Platform, as the repository's rules ask, with Shouldly. They run the real libraries over a small fake of the API, as `IStorytellerDocuments` cannot be faked.

- **The fake** is Kestrel on `http://127.0.0.1:0` (`FrameworkReference` to `Microsoft.AspNetCore.App`), answering JSON by path: `/api/v1/{o}/{p}/{v}/annotations/{key}` with the `AnnotationType` discriminator, `…/annotations/rst.R/executions` and `/usages`, and `…/configuration/{key}/resolved`.
- **Isolated from the environment.** The tests set `Endpoint`, `Key`, `Mode`, `RefreshInterval`, `Optional`, `CacheDirectory` and `Credential` (a fixed test key) in code, as the internal seam of the environment is not visible here. So exported `STORYTELLER_*` variables do not reach them.
- **`DotNet.SimpleConfiguration.UnitTests`:** `ConfigurationDiff`, with `a:01` and `a:1` as two keys; `DocumentKeys` reads the document and not the other sources, and shows an overridden value as `IConfiguration` does; the renderer writes the first render and, after `TryRefreshAsync` over a changed document, the added, changed and removed keys and the new options; a document that does not bind raises no changed key and keeps the options.
- **`DotNet.SimpleExecutor.UnitTests`:** runs `Program.RunAsync` for a responsibility's key and for an execution's key: who, executions, units, units of execution, each format, a unit shared by two subjects written once, a document that fails, a responsibility that does not exist, and the input it refuses with its exit code.

The AppHost has no test project: its wiring is the integration's, which has tests, and `Aspire.Hosting.Testing` is not in the catalog. It is checked by a run.

### 6. Data and documentation

**`src/Platform/Storyteller/Playground/data/`** is a folder for `sform story import`, which reads the kind of a document from its file name and creates missing ancestors:

| File | Content |
| --- | --- |
| `annotations/usg.northwind.playground.json` | `{ "Values": { "plan": "gold" } }` |
| `annotations/usg.contoso.playground.json` | `{ "Values": { "plan": "silver" } }` |
| `annotations/uxe.northwind.playground.production.nightly-export.json` | `{}`: with `exe`, `unt`, `cnt`, `sbt` and `rst` as ancestors |
| `annotations/uxe.contoso.playground.production.nightly-export.json` | `{}` |
| `configurations/rst.playground.conf.json` | `greeting`, `batchSize` `10`, `tags` |
| `configurations/exe.northwind.playground.production.conf.json` | `batchSize` `50` |
| `configurations/unt.playground.nightly-export.conf.json` | `schedule` |
| `configurations/uxe.northwind.playground.production.nightly-export.conf.json` | `target` |

The steps, with the local platform running and its database seeded (`docs/Platform/development.md`), and `sform` signed in as an Administrator of the project:

```powershell
sform account set -p 42dotnet.main -v default
sform story import src/Platform/Storyteller/Playground/data
sform account machine create -a rst.playground -k ApiKey
dotnet user-secrets set Parameters:storyteller-api-key <the key> --project src/Platform/Storyteller/Playground/DotNet.AspireHost/src
```

**New `docs/Platform/Storyteller/playground.md`:** what each project shows, these steps, how to run each project alone and through the AppHost, what to change to see each part react, and the limits above. It warns that the applications print the values of the documents: use a machine without `Secrets.Read`, as the dashboard keeps the console logs too.

**`aspire.md`**, **`hosting.md`**, **`configuration-provider.md`**, **`configuration-formats.md`:** one pointer to the playground each. The "Not wired" line of `aspire.md` stays true of `src/Platform/Aspire.Host`, and gains the pointer.

**`CLAUDE.md`:** `Playground/` in the list of Storyteller's parts.

### 7. Verification

- `dotnet build` of the five projects, without warnings of their own; the two test projects with `dotnet test --project`.
- A run of each application, and of the AppHost with its dashboard, against a throwaway fake of the API outside the repository: a change of a document reaches the renderer, and the variables in the dashboard are those of the integration.
- **The data folder** is read with the platform's own `ViewArchiveNames` and `ViewAnnotationDocument`, once, outside the repository.
- **A run against the real platform stays open.** `src/Platform/Aspire.Host` uses persistent containers and the developer's data, and `sform` needs an interactive sign-in, so it is not started for this. The review says so, and lists the steps for the owner.

### 8. Delivery

One branch, `feat/424-205`, with commits per part: this spec; `DotNet.SimpleConfiguration` with its tests and the data; `DotNet.SimpleExecutor` with its tests; `DotNet.AspireHost`; the documentation; the review.

### Not in scope

- Listing the units of a responsibility in Hosting, and fixing `sform story set`. The review reports both.
- `IStorytellerSubscriptions`, the Key Vault resolver, and the other credentials. The playground can grow them when someone needs them.
- A seed command, or a dependency of the playground's AppHost on the platform's: `sform story import` is enough, and works against a deployed API too.
- Non-.NET examples. The `DotNet.` prefix of the issue leaves room for them.
