# Playground for Storyteller

Linear: [424-205](https://linear.app/424fun/issue/424-205/create-a-playground-for-storyteller).

## Overview

This review covers all of [Playground for Storyteller](../2026-11-06%20Playground%20for%20Storyteller.md), delivered in one branch (`feat/424-205`). `src/Platform/Storyteller/Playground/` now holds three runnable projects and the data they work on:

- **`DotNet.SimpleConfiguration`:** a responsibility's configuration as `IConfiguration`, with a console renderer of every change that reaches `IOptionsMonitor<T>`.
- **`DotNet.SimpleExecutor`:** `IExecutionContext` and `ISubscriptionCatalog`, rendering who it is, executions, units and units of execution, each document in a format of `ConfigurationFormats`.
- **`DotNet.AspireHost`:** both as resources, configured through `42.Platform.Storyteller.Aspire.Hosting`.

**Not verified against a real platform.** Everything ran against fakes of the API; see [Verification](#7-verification), which lists what is left for the owner.

| Commit | Content |
| --- | --- |
| `7e374d11` | The spec |
| `e1ee65ec` | `DotNet.SimpleConfiguration`, its tests, and `data/` |
| `f86f44e2` | `DotNet.SimpleExecutor` and its tests |
| `8fb2c079` | `DotNet.AspireHost` |
| `131c0b63` | `playground.md`, and the pointers in the other documents |
| `2f6f8193` | From the branch review: escaping, the document's own values, test isolation, a stricter test |
| `970fd6b2` | From the branch review: corrections of `playground.md`, and the entry in `CLAUDE.md` |
| This commit | This review |

## What Was Done

### 1. Layout

As planned: `DotNet.SimpleConfiguration/{src,test}`, `DotNet.SimpleExecutor/{src,test}`, `DotNet.AspireHost/src` and `data/`, a folder `/Platform/Storyteller/Playground/` in `42.mono.slnx` with the five projects, and `IsPackable` `false` in each.

### 2. `DotNet.SimpleConfiguration`

- **`Program.cs`** is the spec's snippet, plus `Console.OutputEncoding = new UTF8Encoding(false)`, as a document holds any text and the default encoding of a Windows console or a redirected output garbles it.
- **`PlaygroundOptions`:** `Greeting`, `BatchSize`, `Tags`, bound with `services.Configure<PlaygroundOptions>(builder.Configuration)`.
- **`OptionsRenderer`** writes the active key, the keys and the options at start, and on each `OnChange` the time, one line per changed key and the options. A notification that changed no key says `no key did`.
- **`DocumentKeys`** walks the `StorytellerConfigurationProvider`s of the root, de-duplicating the segments of `GetChildKeys` without case, and keeps the order of the walk.
- **`ConfigurationDiff`**, **`KeyChange`** and **`KeyChangeKind`:** the pure comparison.

**Deviation from the spec: values come from the provider, not from `IConfiguration`.** The spec read each value through `IConfiguration`, to show an overridden value as the application reads it. The branch review found that a document key named `STORYTELLER_API_KEY` then printed the machine's key from the environment. `DocumentKeys` now returns the document's own value. The `Options:` line still shows what the application reads, so nothing is lost for a tester.

### 3. `DotNet.SimpleExecutor`

- **`Program`:** `Main` sets the output encoding and calls `RunAsync(args, console, addStoryteller)`, which holds the `HostBuilder` and catches `CommandParsingException` and the `StorytellerConfigurationException` of the registration, with exit code `2`.
- **`ExecutorCommand : BaseCommand`:** the first argument, `--context`, `--format` and `--watch`, the scope per key type and the exit codes of the spec. One code path serves the one poll and the watch: `WatchExecutionsAsync(R, C, watch ? 10 s : TimeSpan.Zero)`.
- **`ExecutionRenderer`:** the lines on `IExtendedConsole.Console.Out`, and the exact text of each format.
- **`usg.S.R`** filters by subject with the same line as an `exe` key under `--watch`: `key.SubjectName`, which `rst` and `unt` keys leave empty.
- **A one-poll run with no execution** writes `No active execution of R in C.`, which the spec did not list. Otherwise such a run prints nothing.

### 4. `DotNet.AspireHost`

As planned. `AppHost.cs` reads the section `Playground` with `GetRequiredSection`, so a missing section fails at once with its name. The launch profiles use the ports 17394, 15175, 21138, 23135, 22321, 19291, 18326 and 20153, so this host runs next to `src/Platform/Aspire.Host`.

`simple-executor-of-subject` is a third resource next to the two the issue names. It shows `WithExecution` and an execution's who am I, in four lines, and starts only from the dashboard.

### 5. Tests

- **`DotNet.SimpleConfiguration.UnitTests`, 8 tests:** the diff, with `a:01` and `a:1` as two keys; the first render; a refresh over a changed document; the rollback of a document that does not bind, with `Status.IsDegraded`; the keys of the document alone; a key with a line end.
- **`DotNet.SimpleExecutor.UnitTests`, 27 tests:** run through `Program.RunAsync` over a Kestrel fake with the shape of the real routes: who, each kind of rendering, each format, the key types, a shared unit written once, documents that fail, escaped annotation data, and the input it refuses.

**Deviation from the spec: not every setting is in code.** The spec listed `CacheDirectory` among them. It stays unset in the provider's tests, so `STORYTELLER_CACHE_DIR` can reach them: the fake sends no `Cache-Control`, so nothing is written, and its random port makes a new cache identity on each run. The settings that decide a test are in code. `ConfigurePrimaryHandler` turns the machine's proxy off, which the spec did not foresee.

### 6. Data and documentation

- **`data/`** as planned, read by `sform story import`.
- **`docs/Platform/Storyteller/playground.md`** is new, with one pointer each in `aspire.md`, `hosting.md`, `configuration-provider.md` and `configuration-formats.md`, and the entry in `CLAUDE.md`.

### 7. Verification

| Check | Result |
| --- | --- |
| `dotnet build` of the five projects | Succeeded, no warnings from the Playground's files |
| `dotnet test --project` of the two test projects | 8 and 27 passed, also with `HTTP_PROXY` set |
| Each application as a process, against a throwaway fake of the API in Node | The modes, formats and exit codes of the spec. A document change reached the renderer; `batchSize` set to `abc` was rolled back with the provider's warning |
| The AppHost with its dashboard, against the same fake | Four resources in the dashboard. `simple-configuration` showed a document change as a diff, `simple-executor` an `Added` and a `Changed`, and `simple-executor-of-subject` ran once from the dashboard. Every request carried the API key of the parameter, and the who am I lines the keys of `WithStorytellerKey` and `WithExecution` |
| `data/`, read once with the platform's `ViewArchiveNames` and `ViewAnnotationDocument` | Every file parsed as its kind and key, and every annotation file was read without an error |

**What is still open, for the owner.** The local platform was not started: `src/Platform/Aspire.Host` uses persistent containers and the developer's data, and `sform` needs an interactive sign-in. So these have never run against a real API:

1. `sform story import src/Platform/Storyteller/Playground/data`, with its ancestors and `UnitNames`.
2. `sform account machine create -a rst.playground -k ApiKey`, and the three resources under that machine's restriction.

The steps are in [Prepare the data](../../playground.md#prepare-the-data).

**Also not run:** `dotnet test --solution 42.mono.slnx` end to end, as it includes suites that use the Cosmos DB emulator, and the two test projects on Linux.

### 8. Delivery

One branch with the commits above.

## From the reviews

**The plan** was reviewed before its commit, by three independent readers, against the sources:

- **`sform story set` cannot write today**, so the data moved from `story set` commands to a folder for `sform story import`.
- **`ConfigurationKeyComparer`** treats `a:01` and `a:1` as equal, so the keys are not kept in a `SortedDictionary` with it.
- **A failed poll under `--watch` reaches no caller**, so the exit codes were narrowed to what can be implemented, and the limits of the informer were listed.
- **A typed options class** replaced the schema-free one of the first draft, so the rollback of a document that does not bind can be seen, and the application is one to copy. The keys of the document still give every changed line.
- **`--interval`** was dropped, and failures of the first read and of the settings got their own handling.

**The branch** was reviewed by four readers, and each of their 20 findings was checked by a separate reader that tried to refute it. 14 were confirmed:

- **In the code:** keys, titles and usage values were printed raw, so a document or an annotation could forge lines or send escape sequences to the terminal. They are now escaped, for line ends and C0 control characters. The text of a format stays exact, and `playground.md` says so.
- **In the tests:** a proxy of the machine failed them, and `What_RendersThatKindOnly` did not notice a unit of execution leaking into another kind.
- **In `playground.md`:** which machine each key needs, the sample output, the read mode, a cancelled run, and when `UnitNames` is read again.

The 6 refuted findings were preferences or showed no failure. One of them, a key of a document printing a variable of the process, was refuted as an exfiltration path, as nothing carries the output back to the writer of the document. It was still fixed, as the machine's key in a console log is not worth keeping for an example, and the fix made the code simpler.

## For the owner

Two things this branch found and left alone, as they are outside the issue:

1. **`sform story set` fails for every key.** `ToSdkAnnotation` (`src/Platform/Cli/src/Model/AnnotationExtensions.cs`) does not set `AnnotationKey`, which the SDK's model marks `Required.Always`, so serializing the body throws. `StorytellerSetCommand` catches only `ApiException`, so it ends as a crash. The tests mock `IAnnotationsApiClient`, so no test serializes the body. With `AnnotationKey` set, the server would next refuse a body without the type-specific members, such as `SubjectKey`, so the fix has two layers. This was found by reading and by serializing the types, not by a run against a platform.
2. **Hosting cannot list the units of a responsibility.** `StorytellerAnnotation` does not carry `Responsibility.UnitNames`, and the catalog has no read of units. So the executor takes units from the executions' `UnitNames`, and a unit without a unit of execution under a listed execution does not show. A `StorytellerResponsibility` snapshot with `UnitNames`, as `StorytellerExecution` has, would close this; the issue's "render units" is only partly met until then.
