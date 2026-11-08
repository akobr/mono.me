# Playground

The playground is a set of small applications that run the .NET packages of Storyteller against a platform. Use them to try a change of a package, to watch how a change on the platform reaches a process, or as an example to copy.

| Project | Shows |
| --- | --- |
| `DotNet.SimpleConfiguration` | A responsibility's configuration in `IConfiguration` ([the provider](configuration-provider.md)), and every change that reaches `IOptionsMonitor<T>` |
| `DotNet.SimpleExecutor` | Who the process is and what it serves: `IExecutionContext`, `IStorytellerCatalog` and `ISubscriptionCatalog` ([Hosting](hosting.md)), with each document in a format of [ConfigurationFormats](configuration-formats.md) |
| `DotNet.AspireHost` | An AppHost that offers both as resources, configured through [the Aspire integration](aspire.md) |

The code is in `src/Platform/Storyteller/Playground/`, with the data in `data/`. The design and its decisions are in [the spec](specs/2026-11-06%20Playground%20for%20Storyteller.md).

**The applications print the values of the documents**, and an AppHost's dashboard keeps its console logs. Use a machine without `Secrets.Read`, which gets secret bindings as their `@…` expressions.

## Prepare the data

Start the local platform and seed its database first (see [Running the Platform Locally](../development.md)), and sign in with `sform` as an Administrator of the project. Then, from the root of the repository:

```powershell
sform account set -p 42dotnet.main -v default
sform story import src/Platform/Storyteller/Playground/data
sform account machine create -a rst.playground -k ApiKey
dotnet user-secrets set Parameters:storyteller-api-key <the key> --project src/Platform/Storyteller/Playground/DotNet.AspireHost/src
```

- **The import** creates the responsibility `rst.playground`, the subjects `northwind` and `contoso` with their usages and plans, their executions in the context `production`, the unit `nightly-export` and its units of execution, the unit `archive` that no execution runs, and the configurations of `data/configurations/`. It creates the ancestors the files do not name, and reports them with the warning `AncestorCreated`.
- **The machine** is restricted to the responsibility, so it reads the annotation and its descendants. It has `Annotation.Read` and `Configuration.Read`, and no secrets.

## Run through the AppHost

```powershell
dotnet run --project src/Platform/Storyteller/Playground/DotNet.AspireHost/src
```

| Resource | Key | Does |
| --- | --- | --- |
| `simple-configuration` | `WithStorytellerKey("rst.playground")` | Follows the configuration of the responsibility. `STORYTELLER_OPTIONAL` lets it start while the platform is unreachable, and it shows the document as added keys when the platform answers |
| `simple-executor` | `WithStorytellerKey("rst.playground")` | Runs with `--context production --watch`: every subject of the responsibility, and every change of them |
| `simple-executor-of-subject` | `WithExecution("northwind", "playground", "production")` | Started from the dashboard. Runs once, as one subject: who am I of an execution. The machine above reads the ancestors `rst.playground` and `usg.northwind.playground`, and `sbt.northwind` and `cnt.northwind.production` show as `Unreadable`; an unrestricted machine reads all four |

- **Settings.** `appsettings.json` holds `Parameters:storyteller-endpoint` and the section `Playground`: the view, and the names of the responsibility, the subject and the context. Change them to run against another responsibility, or against a deployed API.
- **The API key** is the parameter `storyteller-api-key`. Without it, the dashboard asks for it.
- **Start the platform first.** `simple-executor` ends when who am I fails. Restart it from the dashboard.
- **Both hosts at once.** The launch profiles use other ports than `src/Platform/Aspire.Host`, so the platform's AppHost and this one run side by side.

## Run a project alone

The applications read the variables of [the shared contract](configuration-provider.md#credentials), from the shell as well as from an AppHost:

```powershell
$env:STORYTELLER_ENDPOINT = "http://127.0.0.1:7071/api"
$env:STORYTELLER_API_KEY = "<the key>"
$env:STORYTELLER_KEY = "@42dotnet.main.default.rst.playground"

dotnet run --project src/Platform/Storyteller/Playground/DotNet.SimpleConfiguration/src
dotnet run --project src/Platform/Storyteller/Playground/DotNet.SimpleExecutor/src -- units-of-execution --context production --format env
```

`STORYTELLER_REFRESH_INTERVAL=1s` makes `DotNet.SimpleConfiguration` react at once; the default is 30 s. The tests of both projects run against a small fake of the API, and need no platform.

## `DotNet.SimpleConfiguration`

The smallest application that receives a configuration as `IConfiguration`: `builder.Configuration.AddStoryteller()`, `services.Configure<PlaygroundOptions>(builder.Configuration)`, and `OptionsRenderer`, a hosted service over `IOptionsMonitor<PlaygroundOptions>`.

At the start it writes the key, the keys of the document, and the options. On every change of the monitor it writes one line per changed key, and the new options. Without the `info:` log lines of the host and the provider, which it writes too:

```text
@42dotnet.main.default.rst.playground
  batchSize = "10"
  greeting = "Hello"
  tags:0 = "alpha"
Options: {"Greeting":"Hello","BatchSize":10,"Tags":["alpha"]}
12:03:21 IOptionsMonitor changed
  ~ batchSize = "10" -> "20"
  + note = "new"
Options: {"Greeting":"Hello","BatchSize":20,"Tags":["alpha"]}
```

The typed options name only a part of a document, so the lines come from the keys that the Storyteller provider holds (`DocumentKeys`). Any key added on the platform shows, and the keys of the environment and of `appsettings.json` do not. The lines hold the document's values; an environment variable can still override one for the application, and the `Options:` line shows what the application reads. A line end or a C0 control character in a key or a value is escaped.

| Change it, with `sform` or the admin UI | What it shows |
| --- | --- |
| `sform story config set rst.playground -x batchSize=20` | `~ batchSize`, and the new options |
| `sform story config set rst.playground -x note=new` | `+ note`. The options do not change, as they do not name it |
| A key removed in the admin UI, or a document replaced with `--replace` | `- greeting` |
| `sform story config set rst.playground -x batchSize=abc` | The binding fails, so the provider logs that the document did not bind, and restores the previous one: `IOptionsMonitor changed, no key did` |
| The platform stopped | The provider logs a warning per failed read, and the document stays |

- **A first read that fails** ends the application with the message and exit code `1`: missing settings, a wrong key, a refused credential. Only an unreachable platform starts under `STORYTELLER_OPTIONAL`.
- **A document that does not bind at the start** ends the application with the binder's exception, as the renderer reads the options first.

## `DotNet.SimpleExecutor`

```text
DotNet.SimpleExecutor [all|who|executions|units|units-of-execution]
                      [-c|--context <name>] [-f|--format flat|properties|env|yaml] [-w|--watch]
```

| Argument or option | Default | Meaning |
| --- | --- | --- |
| The first argument | `all` | What to render |
| `--context` | | The context to list. Required for a key without one, refused for a key with one. `who` does not read it |
| `--format` | `yaml` | The format of the documents |
| `--watch` | Off | Keep following the informer, with a poll every 10 s |

The key of the process decides what it lists:

| Key | Lists | Machine |
| --- | --- | --- |
| `rst.R` | With `all` or `units`, the units of `R` first, listed by `IStorytellerCatalog`, so a unit that no execution runs shows too; with `units` and no `--context`, that is all. Then the active executions of `R` in `--context`, with one poll of the informer | Restricted to `rst.R`, or unrestricted |
| `usg.S.R` | The same, of the subject `S` | The same |
| `unt.R.U` | With `all` or `units`, its own document first, and without `--context` that is all. With `--context`, the executions as for `rst.R`, and their units from their `UnitNames` | The same; its own document needs only its key |
| `exe.S.R.C`, `uxe.S.R.C.U` | The execution, by `GetExecutionAsync`, in any status | `who`, `executions` and `units-of-execution` work restricted to the execution. `all` and `units` also read `unt.R.U`, and `--watch` lists through the informer, so they need the machine of `rst.R`; a narrower machine gets `! unt.R.U: …` and exit code `1` for the unit |
| `sbt.S`, `cnt.S.C` | Nothing: they name no responsibility. `who` renders who and exits with `0`; `all` renders who, then exits with `2`; anything else exits with `2` | |

Each document is written under a line with its key, version and format, then the exact text of the format. So a line end or a control character inside the text is the format's: `--format env` keeps control characters in a quoted value as they are. The other lines escape what comes from an annotation.

The mode of the reads is `STORYTELLER_MODE`, `Resolved` when unset, so a document holds what it inherits, with its bindings evaluated. With the machine and the data above and the key `exe.northwind.playground.production`:

```text
Who am I: @42dotnet.main.default.exe.northwind.playground.production
  Type: Execution
  Status: Active
  Units: nightly-export
  Ancestors: rst.playground, usg.northwind.playground
  Unreadable: @42dotnet.main.default.sbt.northwind, @42dotnet.main.default.cnt.northwind.production
Execution exe.northwind.playground.production: Active
  Usage usg.northwind.playground: Active, plan=gold
  Units: nightly-export
--- exe.northwind.playground.production, version 1, yaml
greeting: "Hello"
batchSize: 50
tags:
  - "alpha"
--- unt.playground.nightly-export, version 1, yaml
greeting: "Hello"
batchSize: 10
tags:
  - "alpha"
schedule: "0 2 * * *"
--- uxe.northwind.playground.production.nightly-export, version 1, yaml
greeting: "Hello"
batchSize: 50
tags:
  - "alpha"
schedule: "0 2 * * *"
target: "northwind-archive"
```

Who am I prints what the snapshot of the key's type carries: the units of a responsibility or of an execution, the contexts and responsibilities of a subject, the contexts of a usage, the responsibilities of a context, the type of a unit.

With the key `rst.playground` and `--context production`, the units of the responsibility come first, `archive` and `nightly-export` by key, listed once per run, with `--watch` too. Then the informer gives `Added` for both subjects, by key, so `contoso` comes first. A unit's document is written once per run, as several subjects share a unit, so the executions do not write `nightly-export` again.

**Exit codes and failures:**

| Case | Result |
| --- | --- |
| Everything was rendered | `0` |
| The run was cancelled, with Ctrl+C or a stop of the resource | `0`, whatever failed before. The host does not wait for the command, so without `--watch` the output may stop part way |
| Who am I fails, or the one poll fails, such as for a responsibility that does not exist or a machine that may not list | `1`, with the message on standard error |
| `GetExecutionAsync` finds no execution | `1`, with the message on standard error |
| A document fails, or a format cannot write it | `! {key}: {message}` on standard error, the run goes on, and `1` at the end. Messages name keys and paths, never values |
| The list of the units of `rst.R` fails, such as for a machine restricted below the responsibility | `! the units of rst.R: {message}` on standard error, the executions go on, and `1` at the end |
| One execution or usage cannot be read in the one poll | The informer drops it and logs a warning, so it does not show, and the code stays `0` |
| Under `--watch`, a poll fails | The informer logs a warning and polls again, for ever |
| An unknown option, or what to render, a format, a context or settings that are refused | `2`, with the message on standard error. The settings are read before the command line, so `--help` needs them too |

Logs go to standard error at `Warning` and above, so standard output holds only what the command renders.

**What `--watch` sees** is what [the informer](hosting.md#one-poll) sees: `Added`, `Changed` and `Removed` of the active execution and usage of each subject.

- A configuration write raises no event, as it does not move `Modified`. `DotNet.SimpleConfiguration` is the project that follows a document.
- A new unit of execution raises no event, and `UnitNames` stays old until the execution itself changes, or until a restart. The same holds for the units of the responsibility: they are listed once, at the start (see [Freshness](hosting.md#freshness)).
- A disabled execution leaves the list, as `Removed`. Without `--watch`, an `exe` key still renders it, with its status.

## Limits

- **Units of other keys come from executions.** For a key of `usg`, `unt`, `exe` or `uxe`, the units come from the `UnitNames` of the executions, so a unit that no listed execution runs does not show: listing the units of `rst.R` needs the machine of the responsibility. A process whose key is that unit still renders it.
- **Only an API key.** The AppHost uses `WithApiKey`. Running a project alone takes any credential of [the contract](configuration-provider.md#credentials).
- **No secret resolver.** The applications set no `ReferenceResolver`, so references stay as written (see [Secret references](configuration-provider.md#secret-references)).
- **Not for production.** They print what they read, and the renderer's walk of the provider's keys is there to show every change, not to be copied. Copy the typed binding, and the [worker](hosting.md#a-worker-for-every-subject) for serving subjects.
