# sform Defects Found by the CLI Test Cases

Linear: [424-215](https://linear.app/424fun/issue/424-215/create-cli-automation-testing-project-sform-against-a-real-backend).

## Overview

Phase 4 of [CLI Automation Testing Project](../../../Cli.AutomationTests/specs/2026-11-08%20CLI%20Automation%20Testing%20Project.md) wrote the test case catalogue ([test-cases.md](../../../Cli.AutomationTests/test-cases.md), Testiny project `2SC`) against the code of `sform`, and found defects that it listed as D1 to D6, plus a few paths it avoided:

- refusals of the API that ended with the crash code `1` ("Error occurred: …" or "Total mayhem"), which the catalogue's own rule treats as a failure of every case;
- a certificate renewal that lost the renewed certificate;
- two options of `config set` that did nothing.

This change fixes them before phase 5 automates the cases. A refusal or a failure of the API is no longer a crash anywhere in `sform`:

- one mapping decides the exit code of every status;
- one helper prints the server's message and error code;
- `Program.Main` applies both to whatever a command leaves uncaught.

The renewal is fixed in the API (`Api.Functions`): it now answers the `CertificateRenewalResult` that its OpenAPI document declares, with error codes, and the document describes the certificate as base64. The catalogue and the Testiny cases now expect the fixed behaviour.

Test runs, all green:

| Tests | Before | After |
|---|---|---|
| `Cli.UnitTests` | 1219 (phase 1) | 1294 |
| `Api.Functions.UnitTests` | 2752 | 2758 |
| TypeScript SDK (Vitest) | 336, 1 skipped | 336, 1 skipped (`npm run typecheck` clean) |

The change was made in two passes, an implementation and a review of it. This document describes the result of both.

## What Was Done

### 1. One mapping of API statuses to exit codes

`KeyValueConsoleExtensions.ExitCodeOf` (`src/Platform/Cli/src/Output/KeyValueConsoleExtensions.cs`) is now the rule of the whole CLI. `WriteApiError`, `WriteApiErrorLine` and `Program.Main` all use it.

| Status | Exit code | Before |
|---|---|---|
| `401`, `403` | `44` (`WARNING_UNAUTHORIZED_ACCESS`) | `44` |
| `408`, `429`, any `5xx` but `501` and `505` | `3` (`ERROR_SERVICE_FAILURE`) | `1` |
| any other `4xx` (`400`, `404`, `409`, `412`, `413`, `422`, …) | `2` (`ERROR_WRONG_INPUT`) | `2` for `400`, `404`, `409` and `412`; `1` for the rest |
| `501`, `505` | `2` | `1` |
| anything else (a `2xx` or `3xx` the client could not read) | `1` (`ERROR_CRASH`), a real failure of the contract or the tool | `1` |

`408`, `429` and the `5xx` follow the rule that `sform` already had for token endpoints and discovery. The comment of `ExitCodes.ERROR_SERVICE_FAILURE` says "a retry may help", and that is what `3` means.

`501` and `505` are definitive in the conformance classification (`docs/Platform/Storyteller/conformance.md`). A `501` means a deployment without the feature (`ProtectionNotConfigured`, `MachineAccessNotConfigured`, no renewal service), and no retry changes it. So they are `2`. The first pass had made them `3`; the review changed that so that a CI wrapper that retries on `3` does not retry them.

`KeyValueConsoleExtensions.MessageOf` builds the line of a failed call, and `WriteApiError`, `WriteApiErrorLine` and the new handler of `Program.Main` all use it:

- the server's message, when there is one;
- for a `401` or `403` without one, the general sentence of `ApiRefusal.GeneralMessage`, the same one `Program.Main` prints for a refusal (before: "The request failed with 403 Forbidden.");
- for any other status without one, "The request failed with N Status.", instead of the multi-line text of the exception.

`ReadError` also reads the message and code of an `ApiException<SchemaValidationErrorResponse>`. The client throws one for every `409` of a configuration or schema write (a concurrent change, a deleted annotation), even when the server sent an `ErrorResponse`.

`SchemaValidationConsole.WriteWriteError` is the shared helper of the writes: it lists the failing configurations of a schema violation (exit `2`), and otherwise calls `WriteApiError`.

### 2. `Program.Main` catches what a command leaves

`Program.RunAsync` has a new handler after the `401`/`403` one. It catches an `ApiException` with a `4xx` or `5xx` status, and:

- prints `! <server's message>` (from `MessageOf`) and `  Error code: …`;
- logs only the status: no arguments and no message, because an argument may be the value of a key/value pair;
- ends with `ExitCodeOf`.

Every command that does not catch a status now ends cleanly: machine delete, shared certificate issue, the issuer commands, a `404` or `5xx` while `story export` and `story import` wait. "Total mayhem" is left for real crashes.

### 3. D1: `config set` (and `config edit`, `template set`, `template edit`)

Before, these commands printed "Error occurred: <exception text>" with exit `1` for every refusal but `401`. The template commands handled only `403`. All four now go through `WriteWriteError`:

- a `403` (a Reader, or a machine without `Configuration.Write` or `Template.Write`) is `44`, with the server's message and `Error code: AccessDenied`;
- a `409`, a `412` and the other rejections are `2`;
- a `5xx` is `3`.

The XML docs of the two edit commands list the codes.

Files: `ConfigSetCommand.cs`, `ConfigEditCommand.cs`, `TemplateSetCommand.cs`, `TemplateEditCommand.cs`.

Cases: CFG-SET-12.

### 4. D2: the schema commands

`SchemaOperations.WriteApiError` serves get, set, edit, delete, versions, diff and definition of every schema layer. Before, it mapped only `400` and `404` to `2`, and everything else, `403` included, to `1`. It is now `WriteWriteError`:

- a `403` is `44`;
- the `409` of a concurrent change is `2`, with the server's message. Before, the client's `SchemaValidationErrorResponse` without entries printed the exception text;
- a `500` is `3`.

`SchemaOperationsTests` pinned the old codes: `403` and `500` gave `1`, an undeclared `404` or `413` printed "Error occurred: …", and the `409` of a delete gave `1`. Those tests now pin the new codes, and a theory covers the `403` of each layer.

Cases: SCH-DEF-02, SCH-DEF-03.

### 5. D3: `config patch`

Before, `ConfigPatchCommand.WritePatchError` printed the message of an `ErrorResponse` with exit `1` for every status but `400` and `404`. It is now `WriteWriteError`:

- a failing `test` operation (`412` `PatchTestFailed`) is `2`: the stored content is not what the patch expects, and nothing changed;
- a `409` (a concurrent change, a deleted annotation) is `2`;
- a `403` is `44`;
- a `5xx` is `3`.

The output is the shared one: `! <message>` and `Error code: …`.

Case: CFG-SET-10.

### 6. D4: `story get`

The real path of `story get not-a-key` differs from what the catalogue assumed:

1. A key without a dot matches no route of the read, so the Functions host answers `404` without a body.
2. The command's `404` handler then looks the key up among the deleted annotations. That route takes any text, so it answers `400 InvalidKey` ("Invalid annotation key: not-a-key").
3. That exception was thrown from inside the `catch` block and crashed.

`StorytellerGetCommand` now catches an error of that lookup, except `401` and `403`, and prints it with `WriteApiError`. It also catches every other error of the read itself, for example the `400` of a dotted key that does not parse. Both end with `2`, the server's message and `Error code: InvalidKey`. `401` and `403` stay with `Program.Main`.

Case: ANN-SET-07.

### 7. D5: `account machine renew`

**The contract was wrong.** `CertificatesHttp.PostRenewCertificate` declares `typeof(CertificateRenewalResult)` (`Outcome`, `Pkcs12`, `Password`, `Thumbprint`, `LastRenewalAt`, `Message`), and both SDKs follow it. The handler, however, answered anonymous objects:

- `{ Thumbprint, Certificate, CertificatePassword, LastRenewalAt }` for a renewal;
- `{ Message, LastRenewalAt }` for a certificate that was already renewed.

Neither had an `Outcome`.

**The API fix.** In `Api.Functions`:

- The handler answers a `CertificateRenewalResult`, through the API's System.Text.Json options: `Pkcs12` as base64, nulls left out, `Outcome` as a string.
- The `400` of a renewal before the window carries the new error code `ErrorCodes.NotInRenewalWindow` (`Backend.Core`).
- The `404` (a certificate that is not the machine's current one, or a machine without the expiry of its certificate) carries `ErrorCodes.NotFound`. Before, neither had a code.

**The OpenAPI document.** It described `Pkcs12` (a `byte[]`) as `type: string, format: binary`. The field was never sent before, so the mismatch did not show. With the fix it would have:

- Orval types a binary string as a `Blob`, and its Zod schema (`zod.instanceof(Blob)`) refuses the base64 string the API sends.
- NSwag reads either format into `byte[]`, so the C# SDK was not affected.

The fix has three parts:

1. The new document filter `ByteArraySchemaDocumentFilter` (`Api.Functions/src/OpenApi/Filters`, registered in `OpenApiConfigurationOptions`) changes every binary string property of a component schema to `format: byte`. The binary bodies of the view transfer (`application/zip`) are not component schemas and stay binary.
2. In both copies of `open.api.v0.10.15.json` (`Sdk.NSwag` and `sdk.typescript`), only that property was changed. A full export (`OpenApiExportTests` with `STORYTELLER_OPENAPI_EXPORT`) differs from the committed document only in this format, the version and one description of the patch's `404`, which was already out of date. So the version was kept, and the document was not renamed.
3. The TypeScript SDK was regenerated (`npm run generate`). Three generated files changed, all for `Pkcs12`: the model (`Blob` became `string`), the Zod schema (now `zod.string()`) and the MSW mock. NSwag 14.6.3 run on the changed document produces a `src/ApiSdk.g.cs` identical to the committed one, so the C# SDK is unchanged.

The admin UI does not use the renewal.

**The CLI.** `MachineRenewCommand`:

- writes the renewed `.pfx` (`-o`, or `<id>-renewed.pfx`) and prints its password once, from `Pkcs12` and `Password`;
- also reads the old shape (`Certificate` in base64 and `CertificatePassword`, which the client keeps in `AdditionalProperties`), so the fixed `sform` works against an older API;
- maps a `400` (`NotInRenewalWindow`, whose message names when the window opens), a `404` and a `501` to `2`, with the server's message and code. A `401` stays with `Program.Main`, which ends it with `44`. The OpenAPI document declares the `400` and the `404` without a body, so the message comes from the response text, which `ReadError` parses;
- keeps its output and exit `0` for a certificate that was already renewed: the message and "Last renewed at: …".

**Tests.** `CertificatesHttpTests` has four new tests:

- the JSON of a renewal, with the exact property names and values;
- the JSON of a certificate that was already renewed;
- the `400` with `NotInRenewalWindow`;
- the `404` with `NotFound`.

`ByteArraySchemaDocumentFilterTests` (new) has two: the filter on a document, and the rendered document, which has `format: byte` for `Pkcs12` and only binary zip bodies.

**Docs.** `docs/Platform/Storyteller/authentication.md` describes the renewal: the route, the window and the overlap, the fields of the result (`Pkcs12` as `format: byte`), the refusals with their codes, and the old shape. Its table of access-management errors lists `NotInRenewalWindow` and the renewal's `404`.

Cases: MCH-CRD-05, MCH-CTX-01.

### 8. D6: `config set -c|--custom-properties` and `-l|--labels`

The API has no such fields for a configuration:

- `SetConfiguration` reads the body as the content `JObject` alone;
- `IConfigurationService.CreateOrUpdateConfigurationAsync` takes only the content;
- nothing writes the `Labels` or `Values` of a `ConfigurationEntity`. They come from `ExtendableEntity` and are only mapped on reads.

So the options were removed instead of sent, together with the `// TODO: [P1]` they carried. `config set -c …` is now an unrecognized option (`-1`). Custom properties and labels belong to the annotation (`story set -c|-l`).

Case: CFG-SET-05. It now checks that the four forms end with `-1` and store nothing. Its Covers column is `set`, because the removed options no longer exist for `CommandCoverageTests` to resolve.

### 9. The other crash paths, and the inconsistencies the review found

**Crash paths the catalogue avoided:**

- `account points revoke -r` with a role lower than the member's: the API answers `409 ElevatedRole`, which the client does not declare. `AccessPointRevokeCommand` now catches it and prints it with the server's message, exit `2`. `AccessPointGrantCommand` got the same handler. `401` and `403` stay with `Program.Main`.
- `story kv policy byok-setup` and `byok-verify` on a deployment without a customer-managed key provider: `KeyValueCommandBase` already printed the server's message. With §1 the `501` ends with `2`.
- `account shared-cert issue` in a project without `CertificateAndApiKey` (`400`): caught by `Program.Main` (§2), exit `2` with the server's message.
- A `-i` file that is not a JSON object (not JSON at all, or a JSON array): `JsonInputBuilder` (used by `config set`, `template set` and the schema sets) and `StorytellerSetCommand` now say "The file '…' is not a JSON object: <reason>" and end with `2`. Before, they crashed on the `JsonReaderException`.

**Inconsistencies the review found:**

- `story delete --permanent` printed only "Only an Administrator or Owner can delete permanently." for a `403`, with exit `2`, and dropped the server's message. It now prints the server's message and code, then that sentence, and ends with `44` (`StorytellerDeleteCommand`). Case ANN-DEL-05 changed with it; `docs/Platform/Storyteller/annotation-lifecycle.md` describes it.
- `account points` (`AccessPointListCommand`) treated a `401` like the `404` of an account without a registration: "You account is not registered", exit `43`. The other account commands catch only the `404`. The `401` now goes to `Program.Main` (`44`).
- `story set` printed a `400` or `409` as a bare message, without `! ` and the error code. Before the first pass, a `409` was exit `1`. Every refusal of the write now goes through `WriteApiError`: `400` and `409` are `2`, `403` is `44`, a `5xx` is `3`.

**What remains as `1`:**

- a crash of the tool (an unhandled exception in `Program`, or a host that fails to start);
- the editor of an `edit` command exiting with a non-zero code (`EditorService`);
- the `Failed` status of a `story export` or `story import` job. It is not an API status but the outcome of a job the server ran; its tests pin `1` deliberately.

### 10. Tests

**New: `src/Platform/Cli/test/Commands/ApiRefusalExitCodeTests.cs`**

| Area | What it covers |
|---|---|
| The mapping | Every status, with `501`/`505` as `2` and `502`/`503`/`504` as `3` |
| `Program.RunAsync` | An uncaught `400`, `409`, `501` and `503` |
| `config set` (D1) | `403`; a `403` without a message (the general refusal); `409`/`412`/`500`; a schema violation |
| `config set` (D6) | `-c`, `--custom-properties`, `-l` and `--labels` as unknown options |
| `config patch` (D3) | `412 PatchTestFailed`; `409`/`403`/`503` |
| `config edit` | `403`; `409`/`412`/`500`; a schema violation |
| Templates | `template set` `400`/`500`; `template edit` `400`/`409`/`412`/`500` |
| `story get` (D4) | A `400` of the read; the `404` and `400 InvalidKey` of a key without a dot; a `403` left to `Program.Main` |
| Access points | `points revoke` `409`; `points grant` `400`; `points` `401` left to `Program.Main`, and `404` (not registered) |
| Key/values | `byok-setup` and `byok-verify` `501` |
| `machine renew` (D5) | The documented result; the result of an older API; the client reading the documented JSON; a certificate that was already renewed; `400 NotInRenewalWindow` and `404` with their codes; a `403` without a message; `501`; `401` |
| JSON input | A non-JSON and an array file for `JsonInputBuilder` and for `story set -i` |

**Updated:**

- `SchemaOperationsTests` (§4).
- `ConfigGetCommandTests.OtherApiError_KeepsItsExitCode_OnTheErrorOutput` and `KeyValueBulkAndDeleteCommandTests`: a `500` and a `503` are now `3`.
- `StorytellerSetCommandTests`: the failing lookup prints `! …` and the code, and a theory covers `400`/`409`/`500` of the write.
- `StorytellerDeleteCommandTests.Permanent_WithoutTheRole_ChangesNothing`: `44`, with the server's message, the code and the sentence.

**API:** `Api.Functions/test/CertificatesHttpTests.cs` and `ByteArraySchemaDocumentFilterTests.cs` (§7).

### 11. Documentation and the catalogue

**`docs/42for.net/cli/sform.md`:**

- the exit code table of `story config` (a `5xx` but `501` and `505` is `3`, `1` is a crash only);
- a new section "Write a configuration": the options of `config set` without `-c` and `-l`, and the exit codes of the writes (`40`, `43` and `1` of `patch` and `edit` included);
- a new section "Exit codes" for every command:
  - `43` also covers an account without a registration;
  - `1` covers a crash, a failed export or import job, and an editor that exited with a non-zero code;
  - the messages without a server message;
  - that `story export` and `story import` print no error code.

**`docs/Platform/Storyteller/`:**

- `definitions.md`: the errors of the schema commands follow the mapping. Before, it said that a `403`, a `500` and a `409` exited with `1`.
- `export-import.md`: a `404` while waiting is `2`, and a new row `3` covers a `5xx` while waiting or an unreachable API. Before, both went to `1`.
- `annotation-lifecycle.md`: the `403` of the permanent delete (§9).
- `authentication.md`: the renewal (§7).

**The catalogue (`docs/Platform/Cli.AutomationTests/test-cases.md`):** the list of the defects D1 to D6 and of the avoided paths became one rule that links this review. The note on `sform --version` stays. A crash (`1`) now fails every case without exception. These cases now expect the fixed behaviour:

| Case | Testiny | Now expects |
|---|---|---|
| CFG-SET-05 | 401 | Unknown options, `-1`. Title, steps, Covers and the planned test `Config_CustomPropertiesAndLabels_Rejected` changed |
| CFG-SET-10 | 406 | `412`: `2` with "Error code: PatchTestFailed" |
| CFG-SET-12 | 408 | `44` with "No Contributor access to '<reader project>'." and the code |
| SCH-DEF-02 | 325 | `44` |
| SCH-DEF-03 | 326 | `44` |
| ANN-SET-07 | 292 | `2`, "Invalid annotation key: not-a-key", "Error code: InvalidKey" |
| ANN-DEL-05 | 315 | `44` with the server's message, the code and the sentence; the title changed |
| MCH-CRD-05 | 308 | `renewed.pfx` written, with the password printed once |
| MCH-CTX-01 | 363 | `<id1>-renewed.pfx` written |

The same cases were edited in Testiny `2SC`: their preconditions and steps, and the title of 315. The title of 401 still reads "`-c` and `-l` are accepted and ignored: the write stores only the content" and needs the catalogue's title.

### 12. Formatted JSON no longer folds at 80 columns

The phase 3 runner found that the themed JSON of `WriteJson` (`Output/ExtendedConsoleExtensions.cs`) was rendered by a capture console without a width, so Spectre.Console used 80 columns and folded every longer token inside its string, whatever the terminal's width or `COLUMNS`. A machine's printed key or a long value then reached a reader of the output broken in two. The capture console now has a width of 1,000,000 columns; the terminal wraps long lines itself. The runner of the automation tests still joins such folds (`Running/WrappedJson.cs`), so it reads the output of an older `sform` too.

Not changed: on Windows, `sform` writes redirected output in the console's code page (often the OEM one), not UTF-8. The runner decodes it (`ConsoleOutputDecoder`); switching a redirected `sform` to UTF-8 would change what a pipeline in `cmd` receives, so it is left for a decision of its own.

### 13. D7: `kv import --protection public` and `hyok`

The coverage test of phase 4 found that `kv import` took every level for the keys a `--secret` glob matches: `public` stored those secrets as plain values, and `hyok` was sent and refused by the API, as an import of flat or env text has no values encrypted on the client. Like `kv protect`, `KeyValueImportCommand.ResolveSecretLevelAsync` now refuses both with exit 2 and "The keys --secret matches are imported as secrets: --protection takes standard, hsm or byok." (unit test `Import_SecretsAsPublicOrHyok_IsWrongInput`). `coverage-values.json` lists them under `except`, and `docs/Platform/Storyteller/key-values.md` names the levels.

### Defects found by phase 5

Phase 5 automated the 229 cases against the real `sform` and found five more defects, fixed in §14 to §18. What it found and left is listed under [Not fixed, out of scope](#not-fixed-out-of-scope). After these fixes `Cli.UnitTests` has 1324 tests (28 new), all green, and `CosmosConfigurationServiceTests` of `Backend.CosmosDb.UnitTests` 36 (1 new), all green against a Testcontainers emulator.

### 14. Every `story kv` command crashed (a blocker)

Every `sform story kv …` command line, every `--help` of the tree included, ended with exit `1` and "Total mayhem". The log showed "The constructor of type '…KeyValueListCommand' contains the parameter of type 'System.TimeProvider' is not registered". `KeyValueListCommand`, the parent of every kv command, took `TimeProvider? timeProvider = null`. McMaster's constructor injection ignores the default value of an optional parameter, and `Startup.ConfigureServices` did not register `TimeProvider`. Parsing builds the models of the whole path, so the crash hit every command below the list. The unit tests constructed the commands themselves and never saw it. This blocked GEN-CLI-01, GEN-CLI-03, GEN-CLI-05, the whole Key-Values section (the Elementary KV-PAR-01 and KV-SEC-01 among them), and the setup of CFG-GHA-01 and CFG-GET-02.

**The fix.**

- `Startup.ConfigureServices` registers `TimeProvider.System`.
- `KeyValueListCommand` takes a required `TimeProvider`, so no command keeps an optional parameter.
- Every constructor of the 99 command classes was checked for other parameter types the container lacks: there were none. The other types are the console, the context, the seven API clients, `IFileSystem`, the options, the editor, the browser launcher, `CliPaths`, `SformEnvironment`, `GitHubActionsSettings`, the sign-in services, `TransferPolling`, `ISdkConfiguration`, `IServiceProvider` and McMaster's `CommandLineApplication`.
- `Program.CreateHostBuilder(string[], CliPaths?)` is now internal, so the tests build the host that `Main` builds.

**Tests.**

- `CommandConstructionTests` (new) builds sform's real host through `Program.CreateHostBuilder` and `Startup`. It applies McMaster's conventions with the host's container, as the command line host does, walks the whole command tree and builds the model of every command. It fails with the path and the reason of each command that cannot be built. It also checks that the tree holds every class with `[Command]` of the assembly, and that the container has the system clock. `Startup.ConfigureApplication` sets `JsonConvert.DefaultSettings` for the process, so the class runs in a collection without parallelization (`GlobalJsonSettingsCollection`) and restores the settings afterwards. Without the fix, the test names `sform storyteller kv` and the missing `TimeProvider`.
- `ProgramProcessTests` (out of process, the built sform with every stream redirected):
  - `story kv --help`, `story key-values get --help` and `story kv policy set --help` exit `0` and print `Usage: sform storyteller kv`;
  - `story kv get x -p org1.proj1` and `story kv -p org1.proj1`, as an API-key machine whose `STORYTELLER_ENDPOINT` is a closed port, exit `3` with "The 2S platform could not be reached". Before the fix, all five exited `1`.

Files: `Startup.cs`, `Program.cs`, `Commands/KeyValues/KeyValueListCommand.cs`; `test/CommandConstructionTests.cs`, `test/ProgramProcessTests.cs`.

### 15. `schema … diff --format unified` crashed

`sform story schema type|annotation|descendant diff … --format unified` ended with exit `1` and "! The request failed with 200 OK.". `SchemaOperations.DiffAsync` passed `unified` on to the API, which answers that format as `text/plain` (`DiffResultHttpExtensions.ToDiffResponse`). The typed methods of the NSwag client (`Get*SchemaVersionDiff(Custom)Async`) read a `200` only as `DiffResult` JSON, so they threw an `ApiException` with status `200`, which `ExitCodeOf` maps to `1`.

**The fix**, in the CLI alone. The generated SDK stays as it is: a text method would need a hand-written twin of six methods.

- For `unified` (in any case, with spaces around it), `DiffAsync` asks the API for the JSON diff.
- The new `DiffResultConsoleExtensions.WriteUnifiedDiff` writes it as the text of the API's `DiffFormatter.ToUnifiedDiff`: a `@@ -a,b +c,d @@` header per hunk, then each line with its prefix `+`, `-` or a space.
- The text goes to the standard output through `WriteRaw`, in UTF-8 with LF line ends, so it can be piped.
- A diff without changes writes nothing there, as the API's empty text, and "No changes detected." to the standard error.
- Any other `--format` value, `json` included, still goes to the API as before.
- The help of `--format` says what each value prints.

The config, template and kv diffs send no format, so the API answers them with JSON and they never had the problem.

**Tests.** `SchemaDiffCommandTests` (new, 9 tests):

- the unified text of each layer: type with both versions in three spellings of `unified`, annotation of the latest version, descendant of one version. Each checks the exact output, that the API was asked with no format, and that nothing went to the error output or through the themed lines;
- a diff without changes;
- `--format json` passed to the API;
- `ToUnifiedDiff` with several hunks and with an empty line.

The phase 5 tests SCH-TYP-03, SCH-ANN-03 and SCH-DSC-03 expect exactly this: exit `0`, a hunk header and the changed property.

Files: `Commands/Schemas/SchemaOperations.cs`, `SchemaTypeDiffCommand.cs`, `SchemaAnnotationDiffCommand.cs`, `SchemaDescendantDiffCommand.cs`, `Output/DiffResultConsoleExtensions.cs`; `test/Commands/SchemaDiffCommandTests.cs`.

### 16. `kv export` and `kv get --value-only` wrote indented, wrapped lines

`kv export` without `-o` (`KeyValueExportCommand`) and `kv get --value-only` (`KeyValueGetCommand`) wrote their payload through `Console.WriteLine`. That renders through Alba with a margin of two spaces and wraps lines longer than the width (`COLUMNS`, or 250 when redirected). So `sform story kv export --format env > .env` got indented lines, and a long value, such as a hyok JWE in a json export, was split. The note on secrets without a value was mixed into the document.

**The fix.**

- Both commands write the payload through `WriteRaw`, in UTF-8 (`UseUtf8Output`, as `config --content-only` does).
- `kv export` writes the document with LF line ends and a final one (`KeyValueExportCommand.ToPayload`). It changes only the CRLF that the indentation of the json formats writes on Windows. Every other CR or LF of a value is escaped, and `ReplaceLineEndings` would also have turned the U+0085, U+2028, U+2029 or form feed that a quoted env value keeps as it is into a line end, so the standard output would differ from the `-o` file and an import would read another value.
- `--value-only` writes the value as it is, and a line end.
- The note "N secret(s) without a value were left out of the … output" goes to the standard error (`WriteErrorLine`).
- The `-o` file is written as before.
- The messages of a refused or failed run stay on the standard output, as in every command but `config --content-only`: a `--value-only` without a value (its exit code is listed under [Not fixed](#not-fixed-out-of-scope)), a value that cannot be decrypted, an unknown format and the API's refusals. KV-TRN-04 (`Cli.AutomationTests/src/Tests/KeyValues/KeyValueTransferTests.cs:200`–`:202`) reads the refusal of `kv export --include-secrets` from the standard output. "Output for scripts" in `sform.md` says that only the payload of a successful run is alone there, and that a script checks the exit code.

**Tests.**

- `KeyValueImportExportCommandTests`: the env export prints exactly `DB__HOST=sql\n`, with the note alone on the error output and no themed line; the json export starts with `{\n`, ends with `}\n` and has no CR.
- `Export_ToTheStandardOutput_IsWrittenAsItIs` (new): a 600-character value stays on its line.
- `ToPayload_HasLfLineEndsAndAFinalOne` (new, 5 cases; one keeps U+2028, U+2029, U+0085 and a form feed of a quoted env value).
- `Export_Env_ToTheStandardOutput_KeepsTheUnicodeSeparatorsOfAValue` (new): the env export of such a value prints it as it is, and `ParseEnv` reads it back unchanged.
- `KeyValueSetAndGetCommandTests`: `--value-only` of a public value and of a decrypted hyok value print exactly the value and `\n`.
- `RecordingConsole` now records the standard output of its inner console (`OutputText`).

The phase 5 tests trim lines and parse leniently, so they pass with or without this fix.

### 17. `kv policy set --allow` without `public`

The help of `--allow` said "Public is always allowed", but `KeyValuePolicySetCommand` sent only the given levels. The list replaces the stored one, so `kv policy set --allow byok` sent `[Byok]`, and the API refused it with exit `2` and "Public pairs can not be disabled.".

**The fix.** The command always sends `Public`, first and once (`ToAllowedProtections`: `Prepend(Public).Distinct()`), as the help promises. The help now also says that the levels replace the stored ones. A level still in use has to stay listed: with standard keys, `--allow byok` now gets the API's `409 ProtectionInUse`, also exit `2`. A list that names `public` itself sends the same as before. The tests pass the full list.

**Tests.** `KeyValueProjectCommandTests`:

- `PolicySet_Allow_AlwaysSendsPublic` (4 cases): `byok`, `standard hyok`, `hsm PUBLIC hsm` and `public`;
- `PolicySet_WithoutAllow_KeepsTheAllowedLevels`: no `--allow` sends no list;
- the existing `PolicySet_SendsOnlyTheGivenMembers` with `public standard HSM` is unchanged.

**The catalogue.** KV-POL-07 in `test-cases.md`, and the first step of Testiny 448 (KV-POL-07) and 449 (KV-POL-08), said that `--allow byok` (`hsm`) alone ends with "Public pairs can not be disabled.". They now say that sform adds public, so it ends with exit `2` and `ProtectionInUse`, as standard is still in use. No test runs that command alone.

### 18. The first write of a configuration answered without a `Content`

In the API (`Backend.CosmosDb`), the create branch of `CosmosConfigurationService.WriteConfigurationAsync` returned `configuration.ToConfiguration()`. That takes `Content` from `CalculatedContent`, which a new item does not have. So the API's answer to the first write had no `Content`, and the first `sform story config set|create <key> -x …` (and a `config edit` that creates the configuration) printed a model without it. Every later write returns `ToConfigurationFromContent()`, the stored content.

**The fix.** The create branch returns `configuration.ToConfigurationFromContent()`, the stored content, as every later write does. An import goes through the same method but answers a `ViewImportResult`, so its answer does not change.

**Test.** `CosmosConfigurationServiceTests.FirstWrite_ReturnsTheStoredContent` (new):

- in the project without templates, it checks that no item exists before the write;
- the first write answers version `1` with exactly the written document;
- the second write answers version `2` with the merged document, which a read returns too.

Without the fix it fails on the first write's `Content`. It ran with `STORYTELLER_TESTS_USE_CONTAINER=true`, class by class: 36 of 36.

The phase 5 tests never read `Content` from a first write's printed model. They check its `Version` and read the content back, so they pass either way.

### 19. Findings of the review of PR #178

- **The environment that redirects the token is said.** `SFORM_<section>__<key>` wins over every file, also in a released `sform`, so whatever sets the environment decides where requests and the user's token go. `ConfigurationOverrideNotice` (called by `Program.Main` after the host is built) writes on standard error, once per run, "! The API address comes from the environment, SFORM_general__baseUrl=<value>; sform sends your sign-in there." and "! The sign-in settings come from the environment: <variables>." for `authentication.*`; other variables say nothing. The behaviour stays (the automation tests and CI need it) and `docs/42for.net/cli/sform.md` names it.
- **A network failure names the service that failed.** `RequestTargetHandler`, registered for every client of `IHttpClientFactory` (`ConfigureHttpClientDefaults`), remembers the address of the last request sent. When a request fails without an answer and that address is not the API's (`Program.UnreachableTargetOf`), `sform` prints "! The service at <address> could not be reached: …" and that it called it for the sign-in, instead of pointing at `general.baseUrl`. A failure of the API, or of a client outside the factory, keeps the message of section 2.
- **Small ones:** `machine renew --password-stdin` reads from `System.Console.In` when there is no inner console; `TokenStorage.HomeKey` hashes the folder in lower case on Windows and macOS, whose file systems ignore the case, so one `SFORM_HOME` written in two cases keeps one session; `%%` of a `BROWSER` command line is a literal `%` (`BrowserLauncher.Substitute`); `ExitCodes.ERROR_SERVICE_FAILURE` refers to `KeyValueConsoleExtensions.ExitCodeOf` instead of repeating the mapping.
- **Tests:** `ConfigurationOverrideNoticeTests`, `ProgramTests.UnreachableTargetOf_IsTheServiceOtherThanTheApi`, `BrowserLauncherTests.Substitute_ReadsThePlaceholdersLeftToRight` and `ProtectedTokenStoreTests.HomeKey_IgnoresTheCaseOfThePath_WhereTheFileSystemDoes`.

### Not fixed, out of scope

- `sform --version` prints the version of `42.CLI.Toolkit`, not of `sform` (`Assembly.GetExecutingAssembly()` in the toolkit).
- `-p` without a dot is ignored by the context commands. Only `account set -p` checks the format.
- The OpenAPI document still declares the `400` and `404` of the renewal without a body, although the API sends an `ErrorResponse` with a code. Declaring the body would change the generated NSwag client (`ApiException<ErrorResponse>`) and need a full regeneration of both SDKs. `sform` reads the text, and the TypeScript SDK's fetcher parses any JSON error body.
- The committed OpenAPI document keeps version `0.10.15` and one description that is out of date (the `404` of the patch). The next full export and SDK regeneration will pick both up.
- `story export` and `story import` print a refused start or download (`WriteRefusal`) without the error code. Their exit codes already follow the rule (`44` for `403`, `2` otherwise), and `sform.md` says so.
- The `Failed` status of an export or import job stays `1` (§9).

Found by phase 5, left as they are. Each item says what the tests expect now:

- **History grammar.** `story history` with one hidden entry prints "1 older entries are not shown." (`src/Platform/Cli/src/Output/AuditConsoleExtensions.cs:46`, `WriteHistory`), not "1 older entry is not shown.". ANN-DEL-09 (`Cli.AutomationTests/src/Tests/Annotations/AnnotationDeleteTests.cs:326`) expects the current text for `--top 2` of three entries.
- **The responsibility tree hides a single context.** In `story -t -b rst` (`responsibility`), a subject with exactly one context is shown without it. `StorytellerListCommand.RenderResponsibilityTreeAsync` clears the children of every node with one child (`src/Platform/Cli/src/Commands/Storyteller/StorytellerListCommand.cs:464`), so the tree is not (responsibility, subject, context) as the catalogue says. ANN-LST-06 (`AnnotationListTests.cs:177`) gives the subject two contexts and expects the current behaviour.
- **Mixed exit codes for input errors of the machine commands.** In `MachineCreateCommand`, these end with `-1` (`ERROR_INPUT_PARSING`): an unknown credential kind (`src/Platform/Cli/src/Commands/MachineAccess/MachineCreateCommand.cs:118`), the federation-rule option errors (`:172`, `:178`) and `-s` with another kind (`:195`). These end with `2`: an unknown `--scope` or `-m` value and a runtime scope given to `-m` (`:127`, `:133`, `:141`). `MachineAuthSetCommand.cs:57`, `:69` and `:78` use `-1`. The tests expect the codes as the catalogue documents them: MCH-CRD-02, MCH-CRD-04 and MCH-POL-03 expect `-1`, MCH-LIF-07 and MCH-LIF-09 expect `2`.
- **`machine create --scope SecretsRead` alone gives a machine that cannot read.** `SecretsRead` counts as a runtime scope, so the union of `MachineCreateCommand` (`:144`–`:160`) adds no `-r` scopes, and the server's `MachineScopes.WithImpliedReads` adds no Configuration scope. The machine gets `44` on every `kv get`, `--reveal` included. `sform.md` already says that `--scope secrets-read` alone gives only `Secrets.Read`. KV-SEC-06 (`KeyValueSecretTests.cs:183`, `:185`) creates its machines with `-r --scope SecretsRead`. A later change could add `MachineScopes.ReadOnly` when only Secrets scopes are given, or warn.
- **`kv get <secret> --value-only` without `--reveal` exits `44`.** It prints "The value is a secret; add --reveal." and ends with `WARNING_UNAUTHORIZED_ACCESS` (`src/Platform/Cli/src/Commands/KeyValues/KeyValueGetCommand.cs:68`–`:69`). Nothing was refused (the API answered `200` without the value), so `2` (`--reveal` missing) would fit better. No test depends on it.
- **The arrow of `config patch`.** `PatchVersionReport` (`src/Platform/Cli/src/Json/PatchVersionReport.cs:23`) prints "Configuration for '<key>' patched: version N → M." through `WriteImportant`, without `UseUtf8Output`. On Windows with redirected output, the `→` goes through the OEM code page and may come out as `?` or a best fit. CFG-SET-08 (`ConfigWriteTests.cs:230`, `:240`) matches only "… patched: version N ", never the arrow.
- **Per-instance caches of the API.** Several stores keep a copy per API instance and evict it only on the instance that changed it, so a multi-instance deployment serves stale reads until the copy expires:
  - shared certificates by thumbprint and the project's list, 5 minutes (`src/Platform/Storyteller/Backend.CosmosDb/src/Accessing/CosmosSharedCertificateStore.cs:23`, `:25`): a revoked shared certificate with a valid API key keeps authenticating on another instance for up to 5 minutes;
  - client certificates, 5 minutes (`CosmosClientCertificateStore.cs:21`);
  - federated issuers, 60 seconds (`CosmosFederatedIssuerStore.cs:22`);
  - the machine authentication policy, 60 seconds (`CosmosMachineAuthenticationPolicyStore.cs:15`);
  - the key/value policy, 60 seconds (`src/Platform/Storyteller/Backend.CosmosDb/src/KeyValues/Policy/CosmosKeyValuePolicyStore.cs:20`): key/value writes and reveals check the cached copy.

  The tests re-read with a bounded retry. CRT-SHC-04 reads nothing before it revokes, so no instance has the certificate cached. The lists after a change are re-read within the cache window. KV-SEC-06 and KV-CTX-01 repeat a run until the change applies (`CliTest.EventuallyAsync` with `KeyValuesSupportA.PolicyCacheWait`), and so do the first writes at a level or for recipients the policy has just changed: the hyok writes and re-encryptions of KV-HYK-03 to KV-HYK-06 (`KeyValueRecipientTests.AfterPolicyChangeAsync`) and the byok or hsm `kv set`, `kv protect` and `kv import` of KV-POL-07 and KV-POL-08 (`KeyValuePolicyTests.AtNewLevelAsync`). An older copy refuses such a write (409 `ProtectionNotAllowed`, 400 `UnknownRecipient`) and writes nothing; `GET …/policy` and `GET …/recipients` read past the cache, so sform encrypts for the current recipients. A fix would cache `IsRevoked` for a few seconds only, or invalidate across instances.
- **The refusal of `kv export --include-secrets` does not name the export.** The handler checks the secret read right before it calls the service (`src/Platform/Storyteller/Api.Functions/src/V1/KeyValuesHttp.cs:537`–`:540`), so a caller without it gets the general "The caller has no rights to the secrets of the project." (`KeyValueRequests.cs:87`, 403 `AccessDenied`). The service's own "The caller has no rights to export secrets." (`Backend.CosmosDb/src/KeyValues/CosmosKeyValueService.cs:401`) is never reached on this path. sform prints the server's message correctly. KV-TRN-04 (`KeyValueTransferTests.cs:203`) expects the general message, exit 44 and "Error code: AccessDenied".
