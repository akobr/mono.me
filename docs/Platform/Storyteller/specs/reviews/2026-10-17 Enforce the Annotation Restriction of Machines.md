# Enforce the Annotation Restriction of Machines

## Overview

A machine created with an `AnnotationKey` K now reaches only K and its descendants, in every view of its project. The restriction is read from the machine document in the access check, so it holds for every credential kind. Operations of the whole project answer `403 AnnotationRestricted`. The views and the binding catalog stay readable. New machines get their key validated and stored in canonical form. This implements [Enforce the Annotation Restriction of Machines](../2026-10-17%20Enforce%20the%20Annotation%20Restriction%20of%20Machines.md) in full, in one phase, for Linear 424-48.

## What Was Done

### 1. The rule

Implemented as specified. The one-way rule is "K itself, or `IsDescendantOf(K)`", with the type code compared in any case and the names compared exactly.

Accepted by design, and now stated in `authentication.md`: a descendant of K also inherits from its other parents. For example, `uxe.S.R.C.U` under `exe.S.R.C` merges `unt.R.U`, and a machine restricted to `sbt.S` reaches `usg.S.R`, which merges `rst.R`. The review raised this. It was refuted as the inheritance the spec accepts.

### 2. `MachineAnnotationRestriction`

`Api.Functions/src/Security/MachineAnnotationRestriction.cs` follows the spec, with these details:

- The stored key is exposed as `Key`, not as `AnnotationKey`, because a property named `AnnotationKey` would hide the type inside the class.
- The canonical form lower-cases only the first segment of the parsed key.
- `TryNormalize` treats null or whitespace as valid, with a null result, so `PostMachines` needs one call.
- Refusal messages name the restriction and the requested key, or say that the operation spans the whole project. A stored key that does not parse gives its own message.

`ErrorCodes.AnnotationRestricted` was added to `Backend.Core/src/ErrorCodes.cs`.

### 3. Access checks

`HttpRequestDataExtensions` has one private core, `CheckAccessAsync`, and five entry points:

- `CheckAccessToAsync` and `CheckAccessToProjectAsync`: unchanged signatures. They refuse a restricted machine.
- `CheckAccessToProjectMetadataAsync`: new.
- `CheckAccessToAnnotationAsync`: new.
- `CheckAccessToAnnotationsAsync`: new.

Deviation: `CheckAccessToAnnotationsAsync` returns the restriction (`None` for users). The bulk handlers use it to filter what they answer (section 4).

For machines, the core reads the document with `GetMachineAccessAsync`, then runs `MachineCredentialPolicy.EnsureAllowedAsync` and then the restriction. The machine branch is no longer under `DEV_AUTH`. Only the user role check is, inside `#if !DEV_AUTH`. The container read of `VerifyAccessForMachineAsync` is gone from the request path. `VerifyAccessForMachineAsync` stays on `IAccessService` for the integration tests in `Access.Certificates`.

### 4. Endpoints

These switched to `CheckAccessToAnnotationAsync`, with the same order as before (scope, access, parsing):

- `AnnotationsHttp`: 4 handlers.
- `AnnotationsHttp.Deleted`: 2 handlers.
- `ConfigurationHttp`: all 10 handlers.
- `ConfigurationSchemaHttp`: 7 handlers.

`PrepareSchemaReadAsync` checks the key when the route has one and the project otherwise. That covers the 8 annotation-level and the 4 type-level version endpoints.

`SetAnnotations`, `FindDeletedAnnotations` and `RestoreAnnotations` check the keys of their body. `GetViews` and `GetBindingCatalog` use the metadata check. Everything else still refuses restricted machines through `CheckAccessToProjectAsync`.

Additions found by the review:

- **`RestoreAnnotations`** also restores the deleted ancestors of each key and returned their full stored documents. For a restricted machine, it now leaves out of `Annotations` and `CreatedKeys` every annotation the machine may not address.
- **`SetAnnotations`** creates missing ancestors and listed them in its answer, which showed whether an ancestor outside K existed. Its answer is filtered the same way.

The single `SetAnnotation` answers only the target, so it needed no change. The [authentication docs](../../authentication.md#annotation-restriction-of-machines) describe both filters.

### 5. The `annotationKey` claim

`MachineAuthenticationMiddleware` no longer reads `ClientCertificateValidationResult.AnnotationKey` and no longer adds the claim. The certificate validator still fills the field. Nothing reads it.

### 6. Machine creation

`AccessHttp.PostMachines` normalizes the key before the service is called:

- blank becomes null;
- a valid key is stored in canonical form;
- anything else is `400` `InvalidKey`, and the service is not called.

The operation now documents its `400` response in OpenAPI.

### 7. OpenAPI

Two descriptions now name `AnnotationRestricted`:

- `ResponseForbidden`, the generic 403;
- `ResponseMtlsRequired`, the 403 that the key/value operations declare themselves, so the generic filter skips them.

The committed `open.api.v*.json` snapshots were not regenerated.

### 8. Tests

`Api.Functions/test` has these new or changed files:

- **`MachineAnnotationRestrictionTests`** covers:
  - none and blank keys;
  - K itself for every type;
  - descendants, ancestors, siblings and other branches for every type of K;
  - names as whole segments;
  - case;
  - empty segments;
  - an invalid stored or requested key;
  - `EnsureAllows` and `TryNormalize`.
- **`MachineAccessCheckTests`** covers every entry point for unrestricted, restricted and missing machines, an organization-only key, the document that is read, and the client-credentials policy. Release-only cases check that the new entry points pass the minimal role on to the user role check.
- **`MachineRestrictionHttpTests`** has two parts:
  - In `MachineRestrictionHttpTests.cs`: the configuration, annotation, schema and key/value handlers, the views and the binding catalog, and the filtered answers of bulk create and restore.
  - In `MachineRestrictionHttpTests.Handlers.cs`, two tables:
    - all 31 handlers of one annotation: each refuses an ancestor and a sibling before any service call, and passes the route key to the service for K and a descendant;
    - project-wide handlers: they refuse the restricted machine.
- **`MachineHttpTests`** covers the normalization in `PostMachines`.
- **`RoleAccess`** (`KeyValueTestDoubles.cs`) returns a machine, optionally restricted.

Not planned: `BearerAuthenticationMiddlewareTests` and `MachineTokenFallbackTests` share an xUnit collection now. Both switch the static `BearerValidationMode.DecodeWithoutValidation`, and the larger suite made that existing race fail every run.

14 mutations of the product code were each caught by at least one test. Among them:

- a handler switched to the metadata check or to the project check;
- the key/type split of the schema versions;
- removed answer filters;
- the minimal role dropped (Release);
- the descendant rule removed;
- the lower-casing removed.

**Results:**

| Build | Passed | Failed | Total | Before |
| --- | --- | --- | --- | --- |
| Debug | 723 | 0 | 723 | 348 |
| Release | 717 | 6 | 723 | |

The 6 Release failures are the ones that already failed before this change: five in `MachineHttpTests` and `AnnotationsHttpTests.Purge_ByUser_CallsTheService`. Their user fakes return a role that is too low for the operation.

### 9. Documentation

- **`authentication.md`**: a new section "Annotation restriction of machines", with the table of operations, the inheritance note and the answer filters. It also updates the `AnnotationKey` row, "What each call does" (no container read; every `CheckAccessTo*Async` check), the 401/403 table and the `DEV_AUTH` sentence.
- **`access.md`, `key-values.md` and `configuration-transfer.md`**: describe the refusal. `key-values.md` notes that the Administrator operations answer `AccessDenied` to every machine.
- **`docs/42for.net/platform/admin-ui.md` and `using-the-platform.md`**: describe the restriction and a machine per execution, which needs `-w` to write.

### 10. Review

Two rounds of review ran. Each finding was checked by three skeptics.

- **Round one** had 5 findings. Two held up: the missing tests, now written, and the restore answer, now filtered. Three were refuted:
  - the inheritance of other parents (section 1);
  - `VerifyAccessForMachineAsync` staying on the interface;
  - a comment wording.
- **Round two** had 10 findings. Eight held up and were fixed:
  - the bulk create answer;
  - three doc inaccuracies;
  - the OpenAPI text of the key/value 403;
  - this review;
  - two gaps in handler coverage and one in role forwarding, covered by the tables and the Release-only tests.

  One doc finding was refuted and still applied (the write scope of the per-execution example). One was refuted as intended (the metadata a restricted machine still reads).

Still open, as listed in section 10 of the spec:

- A restricted machine with a configuration write scope can bind any key/value of the view into the configuration of K.
- Client-credentials machines are limited by their token, not by the stored `Scope`.
- `ResetMachineAccessAsync` may write the old API key hash back.
- Certificate renewal writes no new `mcert.*` record.
- `ApiKeyAuthenticationMiddleware` is not registered.
- The access-management operations keep machines out only by scope.
- `Api.Web` is not covered.
- The admin UI mock and `sform machine create` do not normalize the key.
