# Phase 1 of Non-interactive Sign-in for sform

Linear: [424-45](https://linear.app/424fun/issue/424-45/add-non-interactive-cli-sign-in).

## Overview

Phase 1 covers the machine sign-in itself: sections 1–11 of [Non-interactive Sign-in for sform](../2026-10-17%20Non-interactive%20Sign-in%20for%20sform.md).

`sform` reads the `STORYTELLER_*` variables and picks one machine credential from them: an API key, a certificate, both, client credentials, or a token file. It then signs every SDK request with that credential and never touches the stored user session.

Commits:
- `25616646` (feature) and `0548f722` (tests).
- `7f088157` and `88db062d` (fixes from the review of both phases). The fixes that belong to sections 1–11 are described here.

Deviations from the plan:
- `IMachineSignIn` has an extra throwing `GetIdentity()`.
- `-p` to another project drops the view of `access.default.json`, while the same project keeps it.
- `sform account ca` skips the target checks.
- The resolver's own configuration errors use the new `Configuration` reason.

## What Was Done

### 1. Environment variables

As planned. `Authentication/StorytellerVariables.cs` holds the eleven names as `UPPER_SNAKE` constants, like `Constants` and `CommandNames`. Values are trimmed, and an empty value counts as unset.

### 2. Choosing the credential (`MachineSignInSettings`)

As planned. `MachineSignInSettings` is a sealed class with `init` properties, so no generated `ToString` can print a secret; a test checks that.
- `FromEnvironment(Func<string, string?>)` reads the variables and removes a leading `ApiKey ` (any case) from the key.
- `GetKind()` throws `AuthenticationException(Configuration)` with the messages of the spec table.
- **`IsSecureOrLocal` and `IsLocalHttp`.** These static helpers implement the URL rule: `https`, or `http` to `localhost` or a loopback IP literal.
  - The review proposed the `localhost`/literal check because `Uri.IsLoopback` also accepts the host name `loopback`.
  - A check on .NET 10 showed that `Uri` already rewrites that host to `localhost`, so nothing leaked. The stricter helper stays because it is harmless and clearer.
- **`TrySplitProject`.** Rejects an empty organization or project, which `-p` still accepts as before.
- **No variables.** `GetKind()` without any variable throws `No machine sign-in variable is set.`. Callers only reach it in machine mode.

### 3. Machine identity from the credential

As planned.
- **`MachineIdentity`.** A record with the kind, the variable names, the machine ID, the organization, the project and an optional token expiry.
- **`MachineApiKey`.** Copies `StructuredApiKey.TryParse` and keeps no secret, so the record can be printed.
- **`MachineCertificate` and `MachineCertificateIdentity`.** `MachineCertificate.Load` loads PKCS#12 through `IFileSystem` with the default key storage flags. `ReadIdentity` reads the SPIFFE URI SAN with `System.Formats.Asn1` for any trust domain. `MachineCertificateIdentity` is in its own file.
- **Mismatch message.** A key and a certificate of the same project but of different machines get a second message: `The certificate in STORYTELLER_CERTIFICATE_FILE belongs to machine 3f2c but the API key to machine 9a1b.`
- **`JwtExpiry`.** `Authentication/JwtExpiry.cs` replaces `AuthKitAuthenticationService.ReadExpiry`. After the review it also ignores an `exp` that is not a number, which used to throw `InvalidOperationException`. The existing test moved to `JwtExpiryTests`.

### 4. `MachineAuthenticationService`

As planned, with one addition.
- **`GetIdentity()`.** `IMachineSignIn` has `GetIdentity()`, which throws the reason when the variables are invalid, next to `TryGetIdentity()`. `CommandContext` calls it, so a certificate that does not load is reported as such, not as a missing project.
- **Loading.** The credential is loaded once through a `Lazy<>`. A failure is kept and thrown again as a new `AuthenticationException` with the same reason and message on every call.
- **Certificate.** `TryGetCertificate()` returns null unless the certificate loaded with its private key. The certificate is disposed with the service.
- **User name.** `GetSignedInUserAsync` returns `SignedInUser(machineId, "machine <id> (<org>.<project>)", null)`. Without a machine ID it returns `SignedInUser("machine", "machine", null)`.

### 5. Client credentials

As planned.
- **Request.** `POST` of the form, with `scope` only when it is set, through the named client `MachineAuthenticationService`. `Startup` registers that client with `AllowAutoRedirect = false`.
- **Lifetime.** `expires_in` is also read when it is a string. After the review it is capped at one day, so a huge value no longer overflows `AddSeconds`.
- **AuthKit fallback.** It applies only to `client_…` IDs and an `https` `AuthKitDomain`. A discovery that fails counts as "not derivable", and the message asks for `STORYTELLER_TOKEN_URL`. The resolver's own message would point at `app.config.json`, which is wrong for a machine.
- **Errors.** The messages and reasons follow the spec table. A `302` is a `ServiceError` (`failed with 302`), because redirects are not followed.

### 6. Token file

As planned.
- **`Bearer` prefix.** It is removed only when it is followed by white space or ends the file, so a file with only `Bearer ` is reported as empty.
- **Read failure.** A file that exists but cannot be read gets `The token file {path} in STORYTELLER_TOKEN_FILE could not be read.`

### 7. Selection (`AuthenticationServiceSelector`)

As planned. The selector takes `MachineAuthenticationService`. When `IsConfigured` is true, every member goes to it.
- `LogoutAsync` then neither forgets the discovery cache nor clears the user session.
- `Dispose` still disposes only the lazily created user service; the container owns the machine service.

### 8. Target project (`CommandContext`)

As planned, with two changes from the review.
- **Implementation.** `CommandContext` gets `IMachineSignIn` and resolves the organization, project and view in its constructor. `ICommandContext` gets `IsProjectKnown`. `TrySetExplicitTarget` throws `Configuration` for:
  - a malformed `STORYTELLER_PROJECT` without `-p`;
  - a machine without a project;
  - a machine whose target differs from the project of its credential: `The machine credentials in STORYTELLER_API_KEY belong to acme.billing, not acme.web.`
- **Deviation: `-p` and the view of `access.default.json`.** The constructor kept the file's view when `-p` named another project. Now `-p` to another project uses `default` unless `-v` or `STORYTELLER_VIEW` is set. `-p` naming the file's own project keeps its view. The spec only described the constructor's rule.
- **Deviation: `sform account ca`.** `CaDownloadCommand` overrides `ExecutePreconditionsAsync` and skips the target checks. The certificate authority is anonymous and belongs to no project, so a machine without a project can download it.

### 9. Startup wiring (`Startup.cs`)

As planned.
- **`ConfigureMachineSignIn`.** It registers the settings instance, the service with its `IMachineSignIn` forward, the token client, and the `GeneralOptions` post-configure, which applies `STORYTELLER_API_URL` in machine mode only.
- **`ConfigureStorytellerSdk`.** It no longer takes `IConfiguration`. It builds `SdkConfiguration` in the factory from `IOptions<GeneralOptions>` and sets `ClientCertificate = TryGetCertificate()`. It sets `ClientCertificateHeaderName` only for `http` to `localhost` or a loopback literal.
- **`AccessTokenFactory`.** It lets `AuthenticationException` through.

### 10. SDK: certificate header for the local host (`Sdk.NSwag`)

As planned. `ISdkConfiguration.ClientCertificateHeaderName` is a default interface member returning `null`, and `SdkConfiguration` has a settable property. `SdkRequestHelper.ApplyAuthorization` adds the header before the early return for an empty token. A header that is already present is replaced, not duplicated, because the server rejects duplicates with `401`.

### 11. Errors and exit codes

As planned.
- **New reason and exit code.** `AuthenticationFailureReason.Configuration` and `ExitCodes.ERROR_SERVICE_FAILURE = 3`.
- **Mapping in `Program.Main`.** `Configuration` gives `2`, `ServiceError` gives `3`, and the others give `44`.
- **401 and 403.** The new `ApiException` catch returns `44`. In machine mode it adds the hint line, using the `MachineSignInSettings` captured in `InitializeHost`.
- **Narrowed catches.** The catches listed in the spec are narrowed to `StatusCode != 401`, 16 catches in 8 files.
- **Resolver errors (review fix).** `AuthenticationConfigurationResolver` throws `Configuration`, not `ServiceError`, for an unknown `authentication.provider` and a missing `authentication.clientId`. Otherwise those settings errors would end with exit `3` ("a retry may help"). An unknown provider in a server's discovery answer stays `ServiceError`.

### 14. Tests (Phase 1 part)

`src/Platform/Cli/test`, xUnit with Shouldly. Test cases per class, after the review fixes:

| Class | Cases |
|---|---|
| `Authentication/MachineSignInSettingsTests` | 34 |
| `Authentication/MachineApiKeyTests` | 9 |
| `Authentication/MachineAuthenticationServiceTests` | 41 |
| `Authentication/JwtExpiryTests` | 6 |
| `Authentication/AuthenticationServiceSelectorTests` | 4, one new |
| `Commands/CommandContextTests` | 14 |
| `Commands/CaDownloadCommandTests` | 1 |
| `Sdk/SdkCertificateHeaderTests` | 4 |

`Get_Unauthorized_ReachesTheHostAndForbiddenStaysHere` was added to `KeyValueSetAndGetCommandTests`. A `Reason` assertion was added to `AuthenticationConfigurationResolverTests`.

The certificate tests create ECDSA certificates with a SPIFFE SAN and export them to PKCS#12 in a `MockFileSystem` (`TestCertificates`). `ScriptedHttpHandler` now records all request headers and can fail a request (`Fail`).

`dotnet test src/Platform/Cli/test`: **317 passed**, 0 failed, after both phases and the fixes. The baseline before the change was 190; 292 after this phase. The new and changed files build without warnings. The commands were not run against a live API.

The API and the generated NSwag client were not changed.
