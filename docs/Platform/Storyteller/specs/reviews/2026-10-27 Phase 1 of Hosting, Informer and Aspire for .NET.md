# Phase 1 of Hosting, Informer and Aspire for .NET

Linear: [424-91](https://linear.app/424fun/issue/424-91/hosting-featuremanagement-and-aspire-packages-and-the-subscription).

## Overview

Phase 1 covers section 2 of [Hosting, Informer and Aspire for .NET](../2026-10-27%20Hosting,%20Informer%20and%20Aspire%20for%20.NET.md): the server fix that the informer depends on. The annotation lists of the API answered a continuation token that the next request could not parse, and the query dropped the token before it reached Cosmos DB, so every list restarted at its first page. Now every annotation list pages. A follow-up answers a token whose Cosmos DB part Cosmos DB rejects with `400 InvalidQuery` instead of `500`. The final review of the whole branch found the same `500` on the list of deleted annotations, which this phase did not touch; its fix is listed under "Final review".

| Commit | Content |
|---|---|
| `1d999b96` | Section 2: the token's text, its parsing, the Cosmos DB token passed to the query, the page-size seam, the tests |
| `c852ca83` | Follow-up: `400 InvalidQuery` for a bad Cosmos DB part of a token |
| `99074b88` | Final review: the same `400` for the list of deleted annotations |

| Project | Passed (final state of the branch) |
|---|---|
| `Backend.CosmosDb/test`, `ContinuationTokenTests` | 12 |
| `Backend.CosmosDb/test`, `CosmosAnnotationPagingTests` | Needs the Cosmos DB emulator; builds, not run in the final pass |
| `Api.Functions/test` | 2265 |

## What Was Done

### 2. Paging of annotation lists (server)

- **`Backend.CosmosDb/src/ContinuationToken.cs`.** `ToString()` returns the base64 of `{Type}|{CosmosToken}`, the form `TryParse` already read. `TryParse` splits on the first separator only (`Split(TOKEN_SEPARATOR, 2)`), so a Cosmos DB token with a `|` survives. Comments say what the type and the first-page sentinel are.
- **`CosmosAnnotationService.GetAnnotationsAsync<TAnnotation, TEntity>`** takes the Cosmos DB token of the input before it clears the input, maps the first-page sentinel to `null`, and passes it to `GetItemLinqQueryable`, as specified. The token belongs to its type only, so the types after it start at their first page.
- **Page-size seam.** `internal int PageSize { get; init; }`, `CosmosConstants.MaxItemCountPerPage` by default. It is both the `MaxItemCount` of the query and the count after which a page ends at a type boundary. `Backend.CosmosDb.csproj` gained `InternalsVisibleTo` for its test project.
- **Every annotation list** goes through this method (`…/annotations`, the per-type lists, `…/{key}/{descendants}`), so all of them page. The configuration and deleted lists used raw Cosmos DB tokens before and are unchanged.
- **Tests.**
  - `ContinuationTokenTests`, without Cosmos DB: a token's text parses back to its type and Cosmos DB token; the first-page token round-trips; a Cosmos DB token with `|` round-trips; garbage and the old unencoded form do not parse.
  - `CosmosAnnotationPagingTests`, with the project's Cosmos DB collection and a page size of 2: the executions of a responsibility through the descendants route, with expired executions that leave short or empty pages; the `…/executions` list; the multi-type `…/annotations` across a type boundary; the token of a type boundary, which starts that type at its first page and skips the types before it. Every list returns each item once and ends within a page budget.
- **Deviation: the tests use FluentAssertions** (`c852ca83`), as the rest of `Backend.CosmosDb/test` does, instead of adding Shouldly to the project.
- **Beyond the spec: a bad Cosmos DB part answers 400** (`c852ca83`). A token that decodes but whose Cosmos DB part is not a token of the query made Cosmos DB throw a `BadRequest`, which the API answered with `500` and the exception details. The LINQ read now maps it to `InvalidInputException("The continuation token is not valid; list again without it.", ErrorCodes.InvalidQuery)`, so the Cosmos DB message, which quotes the token, stays out of the answer. A test covers it. A token that does not decode still restarts the list, as the spec leaves it.

## Final review

- **The list of deleted annotations answered 500 for a token Cosmos DB rejects** (`99074b88`). `GetDeletedAnnotationsAsync` passes the client's raw token to `AnnotationItemStore.QueryPageAsync`, whose `EnsureSuccess` throws a plain `CosmosException` with the Cosmos DB message; `ErrorResponseMapping` has no case for it, so the answer was `500` with the message, token included, while the other lists answered `400 InvalidQuery`. It now maps a `BadRequest` for a token that was sent to the same `InvalidInputException`, with the message shared as `InvalidContinuationTokenMessage`. The OpenAPI operation `GetDeletedAnnotations` declares the `400`; the committed OpenAPI document and the SDKs follow at the next regeneration, as for other attribute changes. A container test, `ADeletedListTokenThatCosmosRejects_IsInvalidQuery_WithoutTheCosmosMessage`, sits next to the one of the other lists. `annotation-lifecycle.md` says so in the row of `…/deleted`.
