# Review Findings of Data Sets in DbCreator

## Overview

A review of the whole branch of the spec [Data Sets in DbCreator](../2026-11-12%20Data%20Sets%20in%20DbCreator.md) (424-252), after phases 1 to 5 (see their reviews), reported nine findings. An independent check confirmed two of them (rated low by the check, medium by the review); the other seven, all low, came unverified. Each was checked against the code here. All nine held: six were fixed in the code with tests or in the Aspire host, and three were fixed in the documentation, where the code is right and the page was not. None was rejected. The work is the commits `fix(platform): report a key the bulk writer committed as created when its parent is deleted since (424-252)`, `fix(platform): complete the relation arrays of a data set's annotation files after a stopped seed (424-252)`, `fix(platform): refuse a data set with a document or a line over 2 MB before the seed writes (424-252)`, `fix(platform): seed the Aspire showcase for the staging owner without a base account (424-252)` and `docs(platform): bring the page of the data sets in line with the seed's checks, messages and progress (424-252)`.

## What Was Done

### 1. A stopped seed left a parent's relation array without a file's annotation for good (confirmed; fixed)

**The finding.** An annotation file goes through `ImportAnnotationAsync` and `CreateCoreAsync`, which commits the key and its audit in its depth wave and replaces the arrays of the active parents in a later wave (`RelationsWave`). A failure in between that is not a concurrency error (a 503, a 408, a 429 left after the client's retries, a kill or Ctrl+C) ends the run without a record. The next run finds the key active and equal, `ImportActiveAsync` returns `Unchanged` without a look at the parents, and the set is recorded. Nothing repairs the parent later: the bulk writer's repair covers only the closure of the lines, and `core` and `showcase` have no lines. For `core` this is a regression, since the old upsert of `Data.CreateCoreAnnotations` rewrote the arrays on every run.

**Checked.** The paths are as described (`CosmosAnnotationService.cs` 409-537, `BatchPlan` turning other statuses into `CosmosRequestFailedException`, the catch of `AnnotationConcurrencyException` only). A new integration test reproduces the state and fails without the fix.

**The fix.** Neither option of the review as proposed:

- Option 2, a repair in `ImportActiveAsync` for an `Unchanged` or `DocumentExists` key, would add reads of the parents to every import of an existing annotation, the API's import jobs included, and change a path that the spec leaves unchanged.
- Option 1, passing the active file annotations through `CreateMissingAnnotationsAsync`, would decide them all again and could create a key that was purged in between, with an audit entry the seed does not report.

Instead the bulk writer got the repair of its last wave alone:

- `IAnnotationBulkWriter.CompleteRelationsAsync(organization, project, view, IReadOnlyCollection<AnnotationKey> keys, AnnotationBulkOptions? options, CancellationToken)` returns the number of parents it replaced (in a dry run: would replace). Its comment says what it does and what it leaves alone.
- `CosmosAnnotationService.Bulk.cs`:
  - It builds the closure of the keys and their ancestors and reads it in reads of many items (`ReadNodesAsync`).
  - `AddBulkRelations` runs without creates. It adds every active key of the closure to the arrays of its active parents that miss it, the step that already repairs a failed relations wave of the bulk create.
  - `WriteBulkRelationsAsync` replaces each parent once under its ETag, without an audit entry and with `Modified` kept.
  - It creates nothing, and a missing or deleted key or parent stays as it is. DryRun, MaxParallelBatches and MaxUnitsPerPlan count.
  - The retry loop of the bulk create moved to `WithBulkRetriesAsync`, which both methods use: 409 and 412 run the procedure again, a 429 waits its `RetryAfter`, at most 5 attempts. The create's message is unchanged.
- `DataSetSeeder` calls it after the annotation rounds and before the schemas, templates and configurations.
  - It passes the keys of all the annotation files, whatever their outcome. The completion reads the states itself, so a key that is not active changes nothing.
  - A dry run calls it with `DryRun` and counts. A set seeded already is not read, so it is not called, and a set without annotation files does not call it.
  - The count is `DataSetSeedResult.RelationsCompleted`, logged, and printed by `seed` under the set when it is not 0 (`The relation arrays of N annotations completed with the annotation files an earlier run left out of them`).
  - On a first seed nothing is missing, and the step costs a read of many items per 1,000 keys.
  - The lines need no such call: the bulk writer completes its own closure on every call.
- Tests:
  - `CosmosAnnotationBulkWriterTests.CompleteRelations_AddsWhatAnInterruptedCreateLeftOutOfTheArraysOfActiveParents`: two imports whose last wave fails with 503 leave the keys stored and the arrays incomplete, and an import again reports `Unchanged` and leaves them so. Then a dry run counts 2 and writes nothing, and the completion replaces 2 parents after a 412 on its first try. The names are back, `Modified` is kept, there is no audit entry, a missing key is not created, and a second completion writes nothing.
  - `DataSetSeederTests.Seed_CompletesTheRelationArraysOfTheAnnotationFilesAfterTheAnnotations` (over the fake, which records completions): the keys of the files after both rounds of files and lines and before the other documents, the dry run, the count in the result, no call for a set seeded already or without annotation files.
  - `DbCreator/testint` `SeedTests.Seed_AfterARunThatStoppedBeforeTheRelationsOfItsFiles_CompletesTheArrays`: after a seed it deletes the record and takes `invoice` out of `rst.billing` and `prod` out of `sbt.acme` by a raw replace of the items. The next seed finds every document and line unchanged, completes 2 parents, adds no audit entry, records the set, and the run after it finds the set seeded. With the call disabled, the test fails.
- Docs: `data-sets.md` (a new step of [Seeding a set](../../data-sets.md#seeding-a-set), the row of a stopped run in the table of the record, the completion under Bulk writing, the tests) and `annotation-lifecycle.md` (the Data sets section).

### 2. The 2 MB of a document were not checked before the seed wrote (confirmed; fixed in the code)

**The finding.** `data-sets.md` promised the check of the limits "before it writes anything", with exit `2`. `DataSetFolder.Validate` checked only the count of the files and their total size (`ViewArchiveReader.Inspect`). A document over 2 MB failed only when it was read during the seed, after the target was provisioned, the earlier sets were seeded and recorded, and the set's other documents were imported (exit `4`).

**Checked.** As described; `Inspect` never compares one entry with `MaxDocumentBytes`.

**The fix.** The code now keeps the promise, rather than the page being corrected. A set beyond its limits is wrong input in every other case, and a refusal before any write is what the plan is for.

- `DataSetFolder.EnsureDocumentSizes`, called by `Validate` (the plan) and by `ReadDocuments` (the seed and `sets`), refuses a document longer than `ReaderOptions.MaxDocumentBytes` with a `DataSetException` that names the file (`The set '…' cannot be read: the file 'annotations/rst.big.json' is larger than 2,097,152 bytes, the most a document holds.`).
- A document is what the reader takes for one: a name of an export that is not a version file. A version file or a `README.md` may be of any size.
- `sets` now refuses such a set too (exit `2`), as it refuses a set beyond the other limits, instead of listing it with "1 of them cannot be read".
- `ViewArchiveReader` in Backend.Core is unchanged. An import job still fails only the file, and the reader's own check during the read stays as the backstop for a file that grows between the plan and the read.
- Tests: `DataSetFolderTests.ValidateAndReadDocuments_RefuseADocumentLargerThanAnItem` (the message; a document of exactly the limit fits; a version file and a `README.md` over it do not count) and `DataSetSeederTests.Seed_RefusesASetBeyondTheLimitsBeforeAnythingIsWritten` (now also a set with a large file after a dependency: refused before the access model is called).

### 3. A key the bulk writer committed was reported skipped under a parent deleted since (low; fixed)

**The finding.** The bulk create adds children under a stored parent P. Attempt 1 commits child C, and a user deletes P in the meantime. The delete finds its descendants by a query at delete time, so C stays active. The relations wave gets 412, and attempt 2 counts C as missing (`IsKept`), finds P deleted and reports C `Skipped` with `AnnotationDeleted`, although it exists. A sequential import reports it `Created`: `CreateCoreAsync` completes the arrays after its keys committed and finds P not active.

**Checked.** As described: `StateOf` gives P `Deleted`, so the deleted-ancestor check runs before anything else of the kept key. A new test fails without the fix.

**The fix.** A kept key leaves out of what it writes an ancestor that reads deleted. It cannot have been deleted before the call, or attempt 1 would have skipped the key. So the key is reported `Created`, with the warnings of the ancestors it created, the deleted ancestor stays as it is and gets no name, and a key below it that is not written yet is still skipped with `AnnotationDeleted`. The rule of phase 2, "nothing is created under a deleted ancestor", holds for what is written from then on. The test is `CosmosAnnotationBulkWriterTests.CreateMissing_ReportsAKeyItCommittedCreatedAlsoWhenItsParentIsDeletedBeforeTheRelationsWave`: the interceptor deletes the stored `rst.gone` before the unit's batch commits, the relations batch fails with 412 by itself, and the unit is reported `Created`, written once, under the parent that stays deleted. `data-sets.md` says so under Retries.

### 4. A line of `annotations.jsonl` over 2 MB crashed the real run (low; fixed)

**The finding.** No limit applied to a line. A line with 3 MB of `Values` passed the plan and the dry run (exit `0`). In the real run its batch got 413, `BatchPlan` threw `InvalidInputException` (`BodyTooLarge`), and nothing caught it, so the run crashed (exit `1`) on every run.

**Checked.** As described; `AnnotationRules` caps only the title, the description and the documentation link.

**The fix.** The limit of a document applies to a line, as wrong input before anything is written. That is consistent with finding 2 and with the limit of 200,000 annotations, which the same parser already enforces for the plan, `sets` and the seed. A failed line instead (exit `4`), as the review suggested, would come only after the earlier sets of the run were written.

- `DataSetAnnotationLinesReader.MaxLineBytes` (`ViewTransferOptions.MaxAllowedDocumentBytes`).
- `ParseLines` refuses a longer line, in UTF-8 bytes of the trimmed line as written, before it parses it: `'annotations.jsonl' has a line of more than 2,097,152 bytes at line 7, the most a document holds.`
- The annotations of a generator line are a few bytes longer once their placeholders are replaced; the page says so.
- Tests:
  - `DataSetAnnotationLinesReaderTests.Read_RefusesALineLargerThanADocument`: a line of the limit reads, one byte more is refused with its number, multi-byte characters count in bytes, and the plan's count refuses it too.
  - The seeder test of the limits, with a line after a dependency.

### 5. The Aspire resource `db-seed` gave the showcase to an account nobody can sign in as (low; fixed)

**The finding.** `db-seed` got `baseAccount__id` only from the host's `BaseAccount:Id`. Without it, the owner was DbCreator's legacy Entra ID `sub`, which matches no account. `db-creator` makes the AuthKit staging user `user_01M41P6G2TTSXXMM88WNVMRQKG` a second owner of `42dotnet`, so local sign-in works there, but nothing gave that user `fabrikam`. A later seed, also with `--again`, uses the existing project as it is.

**Checked.** As described (`AppHost.cs`, `DataSetOwner.Resolve`, `isProjectCreated = projectPoint is null` in `DataSetProvisioner`).

**The fix.** In `AppHost.cs`, without `BaseAccount:Id` and when the host seeds the staging accounts (`seedStagingAccounts`: the container emulator, or `StagingSeed:Enabled`), `db-seed` gets `WithArgs("--owner", "user_01M41P6G2TTSXXMM88WNVMRQKG")`, the account `db-creator` already makes an owner. The id is written in the host, because its project references are Aspire resources without an assembly reference; the comment names `SeedAccounts.AuthKitOwner`. In the local mode without `StagingSeed:Enabled` nothing changes: the database may be anywhere, and `db-creator` does not seed the staging user there either. The `db-seed` row of `docs/Platform/Aspire.Host/local-development.md`, the Aspire paragraph of `data-sets.md` and step 6.1 of `docs/Platform/development.md` say who the owner is and that the first seed fixes it, and how to change it: reset the volume, or seed with `--organization`. The host builds with its three older `SA1515` warnings.

### 6. An invalid organization name was refused only after the database was touched (low; fixed in the documentation)

**The finding.** `data-sets.md` said that a missing or invalid name of the target exits `2` "before the database is touched". The plan checks the project, a view other than `default` and a dotted organization. The name of an undotted organization is checked in `DataSetProvisioner.ProvisionAsync`, after `EnsureContainersAsync` has created a missing database and containers.

**Checked.** As described.

**The fix.** The page, not the code. Only the database tells whether the organization is new, and an existing organization must stay usable by its name whatever the rules say today (`EnsureNames(isNewOrganization: false)`); a check in the plan would refuse it. The check still comes before anything of the target is written: the account, the access points and the documents. The page now says which names the plan checks and which the seed checks once it reads the access point (The target, step 1 and step 4 of `seed`, and the row of exit `2`). The ensuring of the database and the containers is not a write of the target: `seed` does it first, as the root command does.

### 7. The exit code `2` claimed a refusal of any organization the owner does not own (low; fixed in the documentation)

**The finding.** The row of exit `2` and the comment of `ExitCodes.ERROR_WRONG_INPUT` named "an organization the owner does not own", but `DataSetProvisioner` refuses only a missing project in an organization whose Owner is not the owner. An existing project is used whoever owns it.

**Checked.** As described. The narrower rule is the documented decision of phase 3, and `data-sets.md` already described it under The owner and the creation of the target.

**The fix.** The row and the comment now say "a missing project in an organization whose Owner is not the owner", and the row adds that an existing project is used whoever owns it. The rule is not extended to existing projects. The spec says an existing project is used as it is, and the seed of a project the run did not create is how a shared or older target is completed.

### 8. The quoted message of a set after a failed dependency is not what `seed` prints (low; fixed in the documentation)

**The finding.** The page quoted `The data set 'b' is not seeded: the set 'a' it depends on failed.`, the line of `WriteSummary`, which only the root command prints, and the root seeds only `core`, which has no dependency. `seed` prints `! Not seeded: the set 'a' it depends on failed.` under the heading `# b`.

**Checked.** As described (`DataSetOutput` lines 83 and 208).

**The fix.** The page quotes what `seed` prints, under the heading of the set.

### 9. The sample progress of `seed large` started at 2,000 (low; fixed in the documentation)

**The finding.** The sample output began with `… 2,000 annotations of annotations.jsonl written`. Progress is reported after each plan, and plans are cut within each wave, so the first line of `large` is 506: its depth 0 holds 6 responsibilities and 500 subjects.

**Checked.** As described. The waves of `large` are 506 annotations of depth 0, 4,011 of depth 1 (11 units, 1,000 contexts, 3,000 usages), 6,000 of depth 2 and 5,500 of depth 3, in plans of at most 2,000. The lines are therefore 506, 2,506, 4,506, 4,517, 6,517, 8,517, 10,517, 12,517, 14,517 and 16,017.

**The fix.** The sample shows the first four and the last, and the Progress bullet explains the numbers, with an example that `large` prints.

### Results

- `dotnet test --project src/Platform/Storyteller/DbCreator/test`: 124 of 124 (3 new).
- With `STORYTELLER_TESTS_USE_CONTAINER=true`, on vNext:
  - `DbCreator/testint`: 13 of 13 in 4 min 38 s (1 new).
  - `Backend.CosmosDb/test --filter-class "*CosmosAnnotationBulkWriterTests"`: 21 of 21 (2 new).
  - `--filter-class "*OrganizationsContainerStructureTests"`: 6 of 6. It runs the core seed through `CoreDbStructureBuilder`, and with it the new completion.
  - `Access.Certificates/testint`: 73 of 73. Its fixture seeds `core` too.
- Each of the two new Cosmos tests failed with its fix disabled, and passed with it.
- `Backend.CosmosDb/test`, `DbCreator/test`, `DbCreator/testint` and the Aspire host were rebuilt from scratch. They have the same warnings as before the fixes, compared per file and rule without line numbers.
- The reused vNext container of `backend-cosmosdb-unittests`, up for three days, failed the fixture of a second run with a 500: a statement timeout of its PostgreSQL while it created a container. A `docker restart` of it fixed that. This was unrelated to the change.

## Open points

- **The same gap in the API.** After a create whose last wave failed, a retried `PUT` or a `sform story import` also finds the key active and reports it unchanged without completing the parents' arrays. This is older than this branch. The fix here covers seeds. `CompleteRelationsAsync` is there for the import job too, but calling it there would change the cost of every import, so it is left for a change of its own.
- **An item just under the limit.** A document or a line just under 2 MB can still make an item that Cosmos DB refuses with 413, because the stored item adds stamps and relation arrays, and the batch also carries the audit. Neither the importer nor the bulk writer turns `BodyTooLarge` into a failed document, so such a seed crashes (exit `1`), as an import job does. The limits count the file as written. This is not changed.
