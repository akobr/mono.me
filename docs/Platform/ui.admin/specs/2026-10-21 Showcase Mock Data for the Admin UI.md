# Showcase Mock Data for the Admin UI

Linear: [424-77](https://linear.app/424fun/issue/424-77/new-mock-data-for-admin-ui).

Line numbers refer to the tree this plan was written against (`247c3ccb`). This document does not change any application code.

Inputs to this plan:
- The issue text.
- The six documentation examples in `docs/42for.net/platform/examples/`.
- The Miro board linked from the issue (`uXjVEX-747I=`). For each of eleven examples it has a frame that names the seven types, a frame for the `default` view, and a frame for one other view.
- The CodeRabbit coding plan attached to the issue. It was read as a second opinion. Where this spec decides differently, the section says so; section 15 lists every deviation.

## Problem

The mock mode of the admin UI (`npm run dev:mock`) has two generated datasets:
- `default`: `acme/billing`, `acme/shipping` and `northwind/portal`, about 40 annotations per subject.
- `large`: 16,317 annotations in `acme/billing/default`.

Both are made for tests. Their content looks synthetic:
- Subjects are company names, and every subject has the same responsibilities (`invoicing`, `payments`, `reporting`, …).
- Contexts are `prod`, `staging`, `eu`, `us` and `test`.
- Configurations come from one formula per type.
- No project uses the type names, colors and icons a real project would choose.

The issue asks for:
- A new default dataset that shows how the platform is meant to be used. Each example of the documentation and of the Miro board is its own project, with realistic names, values, configurations, schemas, templates, labels and lifecycle states. The views stand for environments: `development`, `staging`, `release-candidate`, `feature-delivery1` and production (the default view).
- Every existing dataset kept unchanged, and still reachable through options of the mock start-up. The current standard dataset is selected with `--legacy`.
- The signed-in mock user, Ada, able to read every project:
  - Reader in exactly one project.
  - At least Contributor in every other project.
  - Every role level used somewhere.
- Example one (single-tenant B2B SaaS) at about 200 annotations. Example two (multi-tenant B2B SaaS with end customers) at about 1,000.
- The six documentation examples at hundreds of annotations each.
- At least three new examples of my own, with smaller catalogs.

## Current State

### Start-up and datasets

- `package.json:14`: `"dev:mock": "vite --mode e2e --port 5174 --strictPort"`. Extra arguments go to Vite. Vite does not use a `--legacy` flag.
- `app/main.ts:37-40`: in the `e2e` mode it awaits `startMockApi()` (dynamic import) before Pinia, the boot and the mount.
- `app/mock/browser.ts:24-45`: `startMockApi` calls `applyScenarioFromUrl()`, then registers the hand-written handlers in front of `getStorytellerMock()`. It also installs `window.storytellerMock` with `reset`, `changeConfiguration` (default scope `acme/billing/default`, line 21) and `addMachine`.
- `app/mock/db.ts`:
  - `MockScenario` and `scenarios` are at lines 66-68: `default`, `new-user`, `invited`, `large`.
  - The session keys `2s-admin:mock-db` and `2s-admin:mock-scenario` are at lines 69-70.
  - `mockUser` (Ada, `user_01MOCKADA`) is at line 72. `mockDirectory` (Ada, Grace, Alan) is at lines 75-79.
  - `scopeSeed` (lines 113-122) maps four scopes to a subject count.
  - `createSeed` (lines 124-273) builds:
    - the access points and the account, from two literal maps that must agree;
    - two registered views of `acme.billing`;
    - one invitation;
    - the machines, issuers and certificates of `acme.billing`.
    `new-user` and `invited` are derived from `createSeed('default')`.
  - `applyScenarioFromUrl` (lines 280-287) applies `?mock-scenario=`. It sets the scenario key and removes the stored database.
  - `currentScenario` falls back to `default` (lines 289-291). The unit tests rely on that fallback: no unit test sets a scenario.
  - `loadDb` creates the seed again on every request until the first write stores the database (lines 293-299).
  - `annotationsOf` (lines 320-347) caches `generateAnnotations(seed)` in `generated` under `${scenario}:${org/project/view}` and applies the overrides. `overridesOf` (lines 349-351) creates the override record on first use.
- `app/mock/annotations.ts:71-141`: `generateAnnotations` builds about 40 annotations per subject with a seeded mulberry32 PRNG (lines 43-52). Every annotation goes through `withRelations` (line 91).
- `app/mock/documents.ts`:
  - `history(count, content)` (lines 50-65): versions 4 days apart, the newest 1 day old. Versions older than 30 days have no content. Authors cycle through Ada, Grace and Alan.
  - `configurationKeys` (lines 108-122) gives a configuration to a third of the seeded annotations.
  - `baseDocument` (lines 124-160) reads `templates` and `typeSchemas` from `app/mock/schemas.ts`, plus two `northwind` schemas.
  - `documentIds` (lines 201-215) lists the configuration keys, the seven template and type-schema ids, and the two `northwind` schema ids, in every scope.

### Data rules the showcase must respect

- Schemas validate the **stored** document, not the effective one:
  - `nonCompliant` in `template-schema-handlers.ts:192-220` uses `ownContent`.
  - The configuration `PUT` in `configuration-handlers.ts:387` validates the patched own content.
  - `combinedSchema` (`configuration-handlers.ts:186-214`) deep-merges the type schema, the descendant-type schemas of the ancestors and the annotation's own schema. `deepMerge` replaces arrays, so a later `required` replaces an earlier one.
- Roles are exact keys, with no inheritance:
  - The mock: `roleOf` in `handlers.ts:31-33`.
  - The UI: `guards/context.ts:30-40` and `utils/memberships.ts`.
  - A project role without an organization role shows the project ("Project member" on `/orgs`).
  - `syncAccount` (`access-handlers.ts:52-61`) rebuilds `account.AccessMap` from the access points after member changes.
- `GET :org/:project/views` (`handlers.ts:147-157`) returns `db.views[projectKey]` as stored. If a project has no views, it writes an unregistered `default`. The UI shows the `prod` badge on the view named `default` (`utils/views.ts:2-11`). `default` is the fixed, case-sensitive name of the default view in the API.
- Lists are pages of 1,000 items per type (`annotation-handlers.ts:38`). The catalog store is built for 5,000 to 20,000 annotations (`stores/catalog.ts:64-67`). The grid and the mobile list are virtualized.
- The tree sorts children with `localeCompare` (`utils/tree-projection.ts:58,120`), so generated names must not depend on numeric order (`m10` before `m2`).
- `Values` show only in the detail form. `Labels` show in the grid and can be filtered (`label:x`).
- Units need `UnitType` and `UnitDefinition`. `withRelations` fills `event`/`unknown` (`utils/annotation-relations.ts:37-40`). The documented unit types are `time`, `message`, `file` and `event`.

### Deletions and history

- `softDelete` (`annotation-handlers.ts:310-361`) moves the key and its active descendants into `overrides.trash` with one `Deletion`:
  - `DeletionId`, `RootKey`, `DeletedAt`, `DeletedBy`.
  - `IsRoot`, and `DescendantCount` on the root.
  - `PurgeAt` is 90 days later (`audit.ts:24`).
  - Each key also goes to `overrides.deleted`, with a `Deleted` audit entry (`Cause: Cascade` for descendants).
- `GET …/history` (`annotation-handlers.ts:680-711`) adds a synthetic `Created` entry when the stored entries do not start with one.
- A restored key gets its configuration back. A re-created key drops it (`leaveTrashRecreated`, lines 138-144).

### Bindings

- `resolveBindings` (`configuration-handlers.ts:218-235`) only understands `@<annotation key>/<path>`. The UI's explainer flags that form (`binding-language.ts:308-315`).
- The seeded `endpoint: '@config("/baseUrl")'` (`documents.ts:86`) is split into the "key" `config("`. As a result, the Resolved tab of every seeded execution of the `default` dataset answers `422 BindingFailed`.
- The UI documents `@config("<pointer|$.path>")` and `@annotation("<pointer|$.path>"[, "<Type>"])` (`binding-language.ts:128-170`). `ancestorKeyOf` (`binding-language.ts:471-484`) finds the ancestor of a type.

### Project appearance and browser storage

- Project settings live in `localStorage` (`2s-admin:project-settings`, `stores/project-settings.ts:31-33`). The fields are:
  - the type names, colors and icons;
  - the time zone;
  - the date format.
- `sanitizeTypeAppearances` (`utils/type-appearance.ts`) keeps known codes, the 18 palette colors, the bundled icons (`typeIcons`) and names of at most 40 characters.
- The catalog snapshot in IndexedDB (`stores/catalog.ts:61`, key `${user}:${org}/${project}/${view}`) is cleared only on sign-out (`catalog.ts:387-394`). After a dataset change, a snapshot of the same scope from the previous dataset shows until the fresh load replaces it.

### Tests

- 18 unit-test files run `setupServer(...mockHandlers)` on the implicit `default` dataset.
- Every e2e test opens the app through `openApp(page, path, scenario = 'default')` (`test/e2e/support.ts:21-25`), so `?mock-scenario=` is always set. The union in `test/e2e/support.ts:4` is a copy of the one in `db.ts`.
- Playwright's `webServer` (`playwright.config.ts:49-55`) runs `npm run build:e2e && npx vite preview`. Its `env` is merged over `process.env`.
- Build scripts are TypeScript that Node runs with type stripping. Examples: `build/write-environment-config.ts`, run by the deploy job, and `test/unit/build/write-environment-config.test.ts`, which runs it as a child process.

## Proposed Changes

Paths are relative to `src/Platform/ui.admin/src/` unless they start with `docs/`.

### 1. Datasets and launcher options

`MockScenario` gains `showcase`. It is the new default of `npm run dev:mock`. The other datasets keep their content and their names.

| Option of `npm run dev:mock --` | Dataset | Content |
| --- | --- | --- |
| *(none)* | `showcase` | The fourteen projects of this spec |
| `--legacy` | `default` | The current standard dataset (acme, northwind) |
| `--large` | `large` | The current large dataset (16,317 annotations) |
| `--scenario=<name>` or `--scenario <name>` | any `MockScenario` | For example `--scenario=new-user` or `--scenario=invited` |

- **`build/mock-dataset.ts`** (new, pure). Shape:

  ```ts
  export const mockDatasets = ['default', 'new-user', 'invited', 'large', 'showcase'] as const
  export type MockDataset = (typeof mockDatasets)[number]
  export const defaultMockDataset: MockDataset = 'showcase'
  export type DevMockArguments = { dataset: MockDataset; viteArguments: string[] } | { error: string }
  export function parseDevMockArguments(args: readonly string[]): DevMockArguments
  ```

  - It consumes `--legacy`, `--large` and `--scenario` and keeps every other argument for Vite, in order.
  - Two dataset options, a repeated option, `--scenario` without a name, or an unknown name each give an `error` that names the valid datasets.
- **`build/dev-mock.ts`** (new, the runner, like `write-environment-config.ts`):
  - It parses `process.argv.slice(2)`. On an error it prints the message and the usage to stderr and exits with code 2.
  - Otherwise it spawns `process.execPath` with Vite's `bin/vite.js`, resolved through `createRequire(import.meta.url).resolve('vite/package.json')` (Vite does not export `./bin/*`). The arguments are `--mode e2e --port 5174 --strictPort` followed by the forwarded ones.
  - The child gets `env: { ...process.env, VITE_MOCK_DATASET: dataset }` and `stdio: 'inherit'`.
  - The runner exits with the child's code. If the child ends by a signal, it re-raises the signal. It forwards `SIGINT` and `SIGTERM` to the child.
  - It prints one line before Vite starts: `Mock dataset: showcase (npm run dev:mock -- --legacy for the previous one)`.
- **`package.json`**:
  - `"dev:mock": "node build/dev-mock.ts"`, `"dev:mock:legacy": "node build/dev-mock.ts --legacy"`, `"dev:mock:large": "node build/dev-mock.ts --large"`.
  - The `engines` (`node >= 22.18`) already strip types.
- **`app/env.d.ts`**: `readonly VITE_MOCK_DATASET?: string`.
- **`app/mock/db.ts`**:
  - `MockScenario` gains `'showcase'`, and the list is exported as `export const scenarios`.
  - A unit test asserts that `scenarios` equals `mockDatasets`. `build/` cannot import `app/` (no `@/` alias in Node).
- **`test/e2e/support.ts`**: the copied union gains `'showcase'`.

### 2. Start-up precedence, fresh datasets and stale snapshots

In `app/mock/db.ts`, `applyScenarioFromUrl()` becomes `applyStartScenario(launch: MockScenario): boolean`. A pure helper `launchScenario(value: string | undefined): MockScenario` maps the build variable:
- unset or empty → `showcase`;
- a valid name → that name;
- anything else → `showcase`, with a `console.warn`.

At every page load, the precedence is:

1. A valid `?mock-scenario=<name>`: select it and drop the stored database. This is the current behavior. E2E and bookmarks keep working.
2. Otherwise, if the session's launch marker (new key `2s-admin:mock-launch`) differs from `launch`: select `launch` and drop the stored database. A restart with another option therefore takes effect in an open tab, and a new tab gets the launch dataset.
3. Otherwise: keep the session's scenario and database. Reloads keep the user's edits, and so does a `?mock-scenario=` chosen earlier.

In every case the marker is then set to `launch`. The function returns `true` when no database is stored, so the dataset starts fresh.

`startMockApi()` in `app/mock/browser.ts`:

```ts
const fresh = applyStartScenario(launchScenario(import.meta.env.VITE_MOCK_DATASET))
if (fresh) {
  await startDataset(resetDb())
}
```

`startDataset(db)` (new, in `browser.ts`) does two things:
- It awaits `clearCatalogSnapshots()`. This is a new export of `stores/catalog.ts`, and the sign-out hook reuses it. Its failure is ignored, as today.
- If `db.scenario === 'showcase'`, it writes the showcase project settings (section 6.6).

`resetDb()` saves the seed, so the database is stored from the first load. Before, it was stored from the first write. With the stored seed, step 3 and the `fresh` test are reliable, and the timestamps of the seed stay put. `window.storytellerMock.reset(scenario)` becomes `void startDataset(resetDb(scenario))`.

`currentScenario()` keeps its `default` fallback. Unit tests never call `applyStartScenario`, so all of them stay on the `default` dataset.

### 3. Tests stay on the legacy data

- `playwright.config.ts`: `webServer.env: { VITE_MOCK_DATASET: 'default' }`. Playwright merges it over `process.env`, so it works on Windows too.
- With this setting, a load without `?mock-scenario=` before any `openApp` gets the same data as today. No such load exists today. `openApp` keeps its `'default'` default.
- No existing spec, visual baseline or unit test changes.

### 4. The showcase framework

New folder `app/mock/showcase/`:

```
showcase/
  people.ts       personas (id, name, email); merged into mockDirectory
  builder.ts      ScopeBuilder: annotations, documents and deletions of one scope
  history.ts      version histories with the 30-day content rule
  types.ts        ShowcaseProject, ShowcaseOrganization, ScopeBuild
  index.ts        registry: organizations, projects, memoized scopes, lookups
  seed.ts         createShowcaseSeed(): the MockDb of the showcase dataset
  settings.ts     project settings (appearance, time zone, date format) per project
  projects/*.ts   one module per project (section 9)
```

Showcase modules import `db.ts` and `documents.ts` for types only. `db.ts` and `documents.ts` import the showcase at run time, so there is no run-time import cycle.

#### 4.1 `history.ts`

```ts
export interface HistoryOptions {
  /** Days between two versions; default 5. */
  every?: number
  /** Age of the newest version in days; default 1. */
  lastDaysAgo?: number
  /** Authors by version, cycled; default the project's people. */
  authors?: PersonaId[]
}
/** One StoredDocument from contents oldest first; versions older than 30 days keep only their metadata. */
export function versionHistory(contents: Record<string, unknown>[], options: HistoryOptions): StoredDocument
/** `count` contents: `base`, then each version patched (deep merge) by `step`; for long histories. */
export function evolve(base: Record<string, unknown>, count: number, step: (version: number) => Record<string, unknown>): Record<string, unknown>[]
```

`ExpirationTime` is the version date plus 30 days, and `Modified.By` is `accountActor(author)`, as in `documents.ts`. The legacy `history()` stays as it is.

#### 4.2 `builder.ts`

```ts
export interface Lifecycle {
  /** Days from now when the annotation becomes valid; > 0 makes it scheduled. */
  startsIn?: number
  /** Days from now when it expires; < 0 makes it expired, > 0 is an active one with a planned end. */
  expiresIn?: number
  disabled?: boolean
}
export interface Meta extends Lifecycle {
  title?: string
  description?: string          // Markdown
  link?: string                 // DocumentationLink
  labels?: string[]
  values?: Record<string, unknown>
  timeZone?: string             // default: the scope's time zone
  createdDaysAgo?: number       // default 400
  createdBy?: PersonaId         // default: System, like DbCreator's annotations
  modifiedDaysAgo?: number      // default createdDaysAgo
  modifiedBy?: PersonaId
}
export interface UnitMeta extends Meta {
  unitType: 'time' | 'message' | 'file' | 'event'
  definition: string            // a CRON for time, a queue for message, an artifact for file, an event name
}

export class ScopeBuilder {
  constructor(scope: { project: string; view: string; timeZone: string; people: PersonaId[] })
  readonly view: string
  responsibility(name: string, meta?: Meta): string
  unit(responsibility: string, name: string, meta: UnitMeta): string
  subject(name: string, meta?: Meta): string
  context(subject: string, name: string, meta?: Meta): string
  usage(subject: string, responsibility: string, meta?: Meta): string
  execution(subject: string, responsibility: string, context: string, meta?: Meta): string
  unitOfExecution(subject: string, responsibility: string, context: string, unit: string, meta?: Meta): string
  has(key: string): boolean
  configure(key: string, versions: Record<string, unknown> | Record<string, unknown>[], options?: HistoryOptions): void
  template(code: AnnotationTypeCode, versions: …, options?: HistoryOptions): void
  typeSchema(code: AnnotationTypeCode, versions: …, options?: HistoryOptions): void
  descendantSchema(ancestorKey: string, code: AnnotationTypeCode, versions: …, options?: HistoryOptions): void
  annotationSchema(key: string, versions: …, options?: HistoryOptions): void
  /** Deleted `daysAgo` days ago by `by`, with every descendant of the scope (one deletion). */
  trash(key: string, options: { daysAgo: number; by: PersonaId }): void
  build(): ScopeBuild
}

export interface TrashedTree {
  rootKey: string
  annotations: Annotation[]     // root last, like softDelete
  deletedAt: string
  by: PersonaId
}
export interface ScopeBuild {
  annotations: Annotation[]
  documents: ReadonlyMap<DocumentId, StoredDocument>
  trash: TrashedTree[]
}
```

- Keys are built with `buildAnnotationKey` from the SDK, and every annotation goes through `withRelations`. So the keys are canonical and the relation members match the API. Names must be valid key names (no `.`, trimmed).
- Every method throws when an ancestor is missing, or when a key or document is added twice. An authoring mistake therefore fails the unit tests instead of producing a broken tree.
- `Title` defaults to the name in title case. `ProjectName` and `ViewName` come from the scope.
- `ValidFrom`, `ExpiresAt`, `Created.At` and `Modified.At` are relative to `Date.now()`, as in `generateAnnotations`.
- `build()` moves each trashed key and its descendants out of `annotations`. Their configuration documents stay in `documents`, so a restore brings them back.
- The mulberry32 PRNG and the FNV hash of `annotations.ts` are exported (`random`, `hash`) for generated content. The legacy generator does not change.

#### 4.3 `types.ts` and `index.ts`

```ts
export interface ShowcaseView { name: string; description: string }
export interface ShowcaseOrganization { org: string; members: Partial<Record<PersonaId, AccountRole>> }
export interface ShowcaseProject {
  org: string
  project: string
  /** What the project models, for the README and the tests. */
  summary: string
  members: Partial<Record<PersonaId, AccountRole>>   // the project access point; Ada included
  settings: { types: TypeAppearances; defaultTimeZone: string; dateFormat: DateFormat }
  views: ShowcaseView[]                              // `default` first
  /** Fills one view; unknown views stay empty. */
  build(builder: ScopeBuilder): void
  /** Configurations that deliberately fail their combined schema (the non-compliance screens). */
  nonCompliant?: string[]
}
```

`index.ts` exports:
- `showcaseOrganizations` and `showcaseProjects`.
- `showcaseProject(org, project)`.
- `showcaseScope(org, project, view): ScopeBuild | undefined`. It is built lazily and memoized per scope in the module, like `generated` in `db.ts`, and returns `undefined` for an unknown project or view.

#### 4.4 `seed.ts`: `createShowcaseSeed(user: MockUser): MockDb`

- **Access.**
  - `accessPoints` are built from the organizations and the projects, with persona ids. There are no `None` entries.
  - `account.AccessMap` is derived from the access points, as `syncAccount` does, so both maps always agree.
  - Each organization and each project has at least one Owner.
  - All access points allow `ApiKey` and `Federated` machine credentials. `fitness-cloud` and `streaming-platform` also allow `Certificate`.
- **Views.** `db.views[org.project]` holds the project's views, all registered. `default` has `IsDefault: true`, and the others have `IsDefault: false`. `Created` is by the project's first Owner, 300 days ago for `default` and 20 to 200 days ago for the others.
- **Deletions.** For every project, the `default` scope is built and its `trash` goes into `annotations['org/project/default']`:
  - Each tree gets one `Deletion`, shaped as in `softDelete`: `DeletionId` `del_seed_<n>`, `DeletedBy` the persona, and `PurgeAt` = deletion + 90 days. The root has `IsRoot: true` and `DescendantCount`. Every descendant has `IsRoot: false` and `DescendantCount: null`.
  - The keys are listed in `deleted`.
  - `audit[key]` holds `Created` (the annotation's `Created`) and `Deleted`. The `Deleted` entry has `Cause: { Kind: 'Cascade', Key: root }` on descendants and `DescendantCount` on the root.
  - `upserts: {}`.
  - Deletion dates are 3 to 60 days old, inside the 90-day retention. Other views have no seeded deletions.
- **Invitations.** Two pending invitations for realism:
  - A Reader on `fabrikam.fitness-cloud` from Margaret.
  - A Contributor on `seabreeze-hotels.guest-platform` from Grace.
- **Not seeded.** Machines, issuers, certificates, key/value pairs and policies. The existing lazy, empty stores serve them.

`createSeed('showcase')` in `db.ts` returns `createShowcaseSeed(mockUser)`. `new-user`, `invited`, `default` and `large` do not change.

### 5. Routing the showcase through the mock

- **`db.ts` `annotationsOf`.** When `db.scenario === 'showcase'`, the generated base is `showcaseScope(org, project, view)?.annotations ?? []`, under the same cache key. The overrides apply as today. `scopeSeed` and `generateAnnotations` are only used by the other datasets.
- **`documents.ts` `baseDocument`.** For `showcase`, it returns `showcaseScope(...)?.documents.get(id)`. It does not use the legacy configuration heuristic, `templates`, `typeSchemas` or the `northwind` special cases.
- **`documents.ts` `documentIds`.** For `showcase`, the base is the scope's document ids with the prefix. The `configuration:` ids are kept only for keys that `annotationsOf` has, so a deleted annotation's configuration leaves the lists and the non-compliance checks. The legacy configuration list behaves the same way. Stored ids and the existence filter apply as today.
- **No other change.** Handlers, transfers and the configuration list read through these functions, so they need no change. `app/mock/schemas.ts` stays the legacy source.

### 6. Bindings, personas, access and settings

#### 6.1 Bindings in the mock resolver

`resolveBindings(value, db, scope, key)` in `configuration-handlers.ts` gains the two functions the UI documents. The legacy form stays:

| Binding | Resolves to |
| --- | --- |
| `@config("<expr>")` | the value at `<expr>` in `effectiveContent(db, scope, key)`, the snapshot before evaluation |
| `@annotation("<expr>")` | the value at `<expr>` in the `Values` of `key` |
| `@annotation("<expr>", "<Type>")` | the same, of `ancestorKeyOf(key, Type)` |
| `@<key>/<path>` | as today |

- `<expr>` is a JSON Pointer (`/a/b`) or a dotted JSONPath (`$.a.b`).
- A binding that cannot be resolved still answers `422 BindingFailed`.
- Secret sources (`@(path, primaryVault)`), interpolation and math stay unresolved. They are returned as written, so the Resolved tab shows them. The real API needs the vault for them.
- Side effect on the `default` dataset: the seeded `@config("/baseUrl")` now resolves to the context's `baseUrl`, and the Resolved tab of a seeded execution works. No test expects the old 422.

#### 6.2 Personas

`showcase/people.ts` holds the existing three people and nine new ones. All are added to `mockDirectory` in every dataset, so the member names and audit authors show. No handler lists the directory, so the `default` dataset does not change.

| Id | Name | E-mail |
| --- | --- | --- |
| `user_01MOCKADA` | Ada Lovelace | ada@example.com |
| `user_01MOCKGRACE` | Grace Hopper | grace@example.com |
| `user_01MOCKALAN` | Alan Turing | alan@example.com |
| `user_01MOCKMARGARET` | Margaret Hamilton | margaret@example.com |
| `user_01MOCKBARBARA` | Barbara Liskov | barbara@example.com |
| `user_01MOCKEDSGER` | Edsger Dijkstra | edsger@example.com |
| `user_01MOCKRADIA` | Radia Perlman | radia@example.com |
| `user_01MOCKDONALD` | Donald Knuth | donald@example.com |
| `user_01MOCKFRANCES` | Frances Allen | frances@example.com |
| `user_01MOCKKEN` | Ken Thompson | ken@example.com |
| `user_01MOCKHEDY` | Hedy Lamarr | hedy@example.com |
| `user_01MOCKANNIE` | Annie Easley | annie@example.com |

#### 6.3 Organizations and Ada's roles

Ada has a project role in every project. Organization roles are separate (no inheritance), so some organizations show Ada only as a "Project member".

| Organization | Ada's org role | Other org members |
| --- | --- | --- |
| `fabrikam` | Owner | Grace Administrator, Margaret Contributor |
| `bluefin` | — | Frances Owner, Hedy Administrator |
| `streamwave` | Administrator | Ken Owner, Radia Contributor |
| `ledgerline` | Owner | Barbara Administrator |
| `aurora-motors` | Contributor | Hedy Owner, Annie ContributorWithSecrets |
| `harbor-industrial` | — | Radia Owner, Ken Reader |
| `meridian-health` | — | Annie Owner, Barbara Administrator |
| `appforge` | ContributorWithSecrets | Edsger Owner, Hedy Contributor |
| `gatherly` | Owner | Donald Administrator |
| `pixel-forge` | Administrator | Alan Owner, Ken Contributor |
| `velvet-retail` | Contributor | Margaret Owner, Edsger Administrator |
| `seabreeze-hotels` | — | Grace Owner, Frances Reader |

| Project | Example | Ada | Other project members |
| --- | --- | --- | --- |
| `fabrikam/finance-cloud` | 1, single-tenant B2B SaaS | Owner | Grace Administrator, Alan Contributor, Margaret ContributorWithSecrets, Ken Reader |
| `fabrikam/fitness-cloud` | 2, multi-tenant B2B SaaS | Administrator | Margaret Owner, Barbara Contributor, Annie Reader |
| `fabrikam/invoicing-suite` | 3, B2B SaaS (docs) | ContributorWithSecrets | Grace Owner, Edsger Administrator, Alan Contributor |
| `bluefin/money-suite` | 4, B2C finance (docs) | Contributor | Frances Owner, Hedy ContributorWithSecrets, Radia Reader |
| `streamwave/streaming-platform` | 5, distributed platform (docs) | Administrator | Ken Owner, Radia Administrator, Donald Contributor, Grace Reader |
| `ledgerline/erp` | 6, versioned monolith (docs) | Owner | Barbara Administrator, Edsger Contributor, Alan Reader |
| `aurora-motors/vehicle-platform` | 7, physical product (docs) | Contributor | Hedy Owner, Annie ContributorWithSecrets, Frances Reader |
| `harbor-industrial/plant-fleet` | 8, device fleet (docs) | ContributorWithSecrets | Radia Owner, Ken Contributor, Margaret Reader |
| `meridian-health/clinical-integrations` | 9, hospital network (board) | **Reader** | Annie Owner, Barbara Administrator, Donald ContributorWithSecrets, Grace Contributor |
| `appforge/white-label-app` | 10, white-label app (board) | Contributor | Edsger Owner, Hedy Administrator, Alan Reader |
| `gatherly/conferences` | 11, conference series (board) | Owner | Donald Administrator, Frances Contributor |
| `pixel-forge/live-ops` | 12, game live operations (new) | Administrator | Alan Owner, Ken ContributorWithSecrets, Barbara Contributor |
| `velvet-retail/storefronts` | 13, multi-brand storefronts (new) | Contributor | Margaret Owner, Edsger Administrator, Radia Reader |
| `seabreeze-hotels/guest-platform` | 14, hotel distribution (new) | ContributorWithSecrets | Grace Owner, Frances Administrator, Hedy Contributor, Annie Reader |

Ada's project roles:
- Owner in 3 projects, Administrator in 3, ContributorWithSecrets in 3 and Contributor in 4.
- Reader only in `clinical-integrations`, a regulated domain where changes go through change control.

Every level from Reader to Owner is used:
- among Ada's project roles;
- among the other people's roles at organization level;
- among the other people's roles at project level.

#### 6.4 Views

Every project registers the five environment views, in this order:

| View | Description (default) | Content rule |
| --- | --- | --- |
| `default` | Production environment | The full catalog |
| `development` | Development environment | A small catalog with developer contexts (`dev`, `qa`, test rigs, test members) and developer values (debug logging, sandbox endpoints) |
| `staging` | Staging environment | Test data of moderate size: test tenants or members, the staging instances |
| `release-candidate` | Release candidate | A close copy of production with the next release's changes: newer configuration versions, a tightened schema, one new responsibility where it fits |
| `feature-delivery1` | Feature delivery 1 | A small subset plus one new feature (a responsibility or a unit) |

- The view highlighted on the Miro board for an example follows the board frame, extended where the frame is only a slice.
- Each project may refine a description, for example `feature-delivery1` "ADAS on the Touring trim".
- Two documentation pages name a view of their own. Their projects register it as a sixth view:
  - `playback-14` in `streaming-platform`, the release candidate of the playback service;
  - `campaign-march` in `plant-fleet`, a diagnostic week with a faster sample rate.
- The default view keeps the name `default`. The UI and the API identify it by that name and show it with the `prod` badge. Its description reads "Production environment", so it stands for production as the issue asks.
- The issue's `feature-devivery1` is read as `feature-delivery1`.

#### 6.5 Size

The `default` views add up to about 3,600 annotations. Non-default views are built only when they are opened. No type stream reaches the 1,000-item page.

#### 6.6 Project settings

`showcase/settings.ts` exports `writeShowcaseSettings(storage = localStorage)`. For every showcase project it replaces the entry `org.project` in `2s-admin:project-settings` with the project's `settings`:
- the type names, plurals, colors and icons;
- the default time zone;
- the date format.

Other entries are kept.

It runs only when the showcase dataset starts fresh (section 2): at the first load of a tab, after a launch change, after `?mock-scenario=showcase`, or on `storytellerMock.reset('showcase')`. So:
- The appearance is part of the dataset, and resetting the dataset restores it.
- The user's changes in Settings are kept across reloads, like any project setting.
- "Reset to built-in" in Settings shows the built-in names until the dataset is reset again.
- The legacy project keys (`acme.*`, `northwind.*`) are never written.
- `stores/project-settings.ts`, `settings.vue` and `TypeAppearanceEditor.vue` do not change.

The values are checked by a unit test. `sanitizeTypeAppearances(settings.types)` must equal `settings.types`, so every color is in the palette, every icon is bundled, every name has at most 40 characters, and every plural comes with a name.

### 7. Content rules for every project

In the `default` view of every project:

- **Lifecycle.** Every status appears:
  - active;
  - scheduled (`startsIn > 0`);
  - expired (`expiresIn < 0`);
  - disabled;
  - deleted (at least one tree with descendants in Deleted, and one deleted leaf).
  Most annotations are active. Some active ones have a planned `ExpiresAt` (a contract end, a support end).
- **Configurations.** About half of the annotations have an own configuration. Every type that the project uses gets a configuration, except that types without a meaningful document (often uxe) may have none. Each project has at least:
  - one configuration with 1 version;
  - one with 2-5 versions;
  - one with 15 or more versions.
  Versions are spaced so that recent versions show content and versions older than 30 days show only metadata. Authors are the project's members with at least the Contributor role.
- **Templates.** At least one meaningful type template (usually `exe` or `uxe`). Its values are defaults that the executions inherit, such as retries, log level or a grace period.
- **Schemas.** At least one meaningful schema: a type schema, a descendant-type schema on an ancestor, or an annotation schema. Every own configuration under a schema satisfies the combined schema as a stored document, except the keys listed in `nonCompliant`. `streaming-platform` lists one, to show the non-compliance screens. `required` lists are written for the whole merge (`deepMerge` replaces arrays).
- **Bindings.** Where the domain has them, configurations use `@config("…")`, `@annotation("…", "<Type>")` and, for secrets, `@(path, primaryVault)`.
- **Labels and values.** Labels group annotations (segment, region, tier, `lts`, `edge`, `ml`, …). Values carry facts that are not configuration (CRM id, e-mail, serial number, owner).
- **Names.** Names are lowercase with digits and hyphens. The documentation's own names are kept as written, `vin-4F2` included. Generated names never rely on numeric order. Time zones are realistic per subject or context.
- **Determinism.** Generated content uses the seeded PRNG keyed by the project and view, so the same build gives the same keys and values.

### 8. Type appearance per project

The appearance names come from the Miro board for examples 1-11. Plurals are given where `pluralize` would be wrong. Colors and icons are chosen per project from the palette and `typeIcons`.

| Project | rst | unt | sbt | usg | cnt | exe | uxe |
| --- | --- | --- | --- | --- | --- | --- | --- |
| finance-cloud | Application | Job | Customer | Usage | Instance | AppRun | JobRun |
| fitness-cloud | Application | — | Tenant | App enablement | Member | App assignment | — |
| invoicing-suite | Module | Job | Customer | Contract | Environment | Deployment | JobRun |
| money-suite | Product | Batch | Person (People) | Entitlement | Legal entity | Product use | BatchRun |
| streaming-platform | Service | Pipeline | Market | License | Cell | Deployment | PipelineRun |
| erp | Module | Job | Version | Build | Channel | Rollout | JobRun |
| vehicle-platform | Subsystem | Firmware (Firmware) | Model | Fitment | Build | Fitted part | Firmware pin |
| plant-fleet | Capability | Job | Site | Enablement | Device | Assignment | JobRun |
| clinical-integrations | Clinical system | Interface | Hospital | License | Department | Integration | Feed |
| white-label-app | Feature | Campaign | Brand | Feature license | Platform | App feature | CampaignRun |
| conferences | Event service | Job | Event series (Event series) | Booking | Edition | Edition service | JobRun |
| live-ops | Backend service | Live event | Game | Service plan | Platform region | Service deployment | Event run |
| storefronts | Capability | Feed | Storefront | Subscription | Market | Market setup | Feed run |
| guest-platform | Module | Sync job | Hotel | License | Channel | Channel setup | Sync run |

### 9. The projects

Each subsection lists the model, the entities, the documents and the views. The documentation projects keep the documentation's names, values and rules exactly, and add more of the same kind.

#### 9.1 `fabrikam/finance-cloud`: single-tenant B2B SaaS (example 1, about 200)

Fabrikam sells a finance back-office suite. Each customer gets its own instances, because the product is not multi-tenant.

- **Subjects.** 12 B2B customers:
  - `contoso`, from the board;
  - `tailspin-toys`, `wide-world-importers`, `adventure-works`, `litware`, `coho-winery`, `proseware`, `lucerne-publishing`, `margies-travel`, `alpine-ski-house`, `woodgrove-bank` and `fourth-coffee`.
  - Values: `crmId`, `supportTier` (`standard`, `premium`, `enterprise`), `country` and `accountManager`. Labels: segment (`smb`, `mid-market`, `enterprise`) and region (`emea`, `americas`, `apac`).
- **Contexts.** 2-4 instances per customer: `demo`, `production`, `production-eu`, `production-us`, `uat` and `training`. Configuration: `hostname` (`<customer>.finance-cloud.fabrikam.com`), `region` (cloud region), `productVersion` (`2026.9.x`) and `databaseTier`.
- **Responsibilities.** 8 applications:
  - `invoicing`, `payments` and `reporting`, from the board;
  - `expenses`, `payroll`, `tax-filing`, `bank-sync` and `customer-portal`.
  - Configuration: product defaults.
- **Units.** Jobs, only where an application needs them:
  - `reporting.nightly`, from the board;
  - `invoicing.dunning-run`, `payments.settlement-export`, `bank-sync.statement-import`, `payroll.payslip-run` and `tax-filing.vat-submission`.
  - `time` units with a CRON definition.
- **Usages.** The applications each customer bought, about 50.
- **Executions.** An application activated in one instance, about 70. Not every bought application is active in every instance (a `demo` runs fewer).
- **Units of execution.** A job in one instance, about 30. Configuration: CRON override, `batchSize` and `timeoutMinutes`.
- **Template.** `exe`: `logLevel: info`, `retries: 3` and a `telemetry` block (endpoint and sampling).
- **Schemas.**
  - The `cnt` type schema requires `region` and `hostname`.
  - A descendant-type schema on `rst.invoicing` for `exe` requires `taxProvider`.
- **Lifecycle.**
  - An expired trial instance: `cnt.litware.trial`.
  - A scheduled go-live: `cnt.woodgrove-bank.production`, with its executions.
  - A disabled execution in UAT.
  - A churned customer in Deleted with all its instances, usages and executions: `sbt.margies-travel`.
  - A deleted leaf: one unit of execution.
- **Histories.** `rst.invoicing` has 18 versions. `cnt.contoso.production` has 4.
- **Views.**
  - `development` follows the board: `contoso` with `dev` and `demo`; `invoicing` and `payments`; `exe.contoso.invoicing.dev`, `exe.contoso.invoicing.demo` and `exe.contoso.payments.demo`. It adds the `dev` job of reporting.
  - `release-candidate` is production with `productVersion` `2026.10.0`.
  - `feature-delivery1` has two customers and a new application, `ai-reconciliation`.

#### 9.2 `fabrikam/fitness-cloud`: multi-tenant B2B SaaS with end customers (example 2, about 1,000)

Gym chains buy a multi-tenant platform and sell its applications to their members.

- **Subjects.** 6 tenants (gym chains):
  - `fitfactory`, from the board;
  - `pulse-gyms`, `yoga-loft`, `aqua-arena`, `ironworks` and `family-fit`.
  - Values: `legalName`, `country`, `currency` and `supportTier`.
- **Contexts.** About 550 members, generated with the PRNG from Czech, German, Slovak and English name pools:
  - The board's `jnovak`, `kschwarz`, `mdvorak` and `epetrova` under `fitfactory`.
  - `Name` is the username (first initial and surname, lowercase, ASCII, with a letter suffix on a clash). `Title` is the full name.
  - `Values` always holds `email=<username>@<mail domain>`, plus `memberSince`.
  - Labels group members: tier (`basic`, `plus`, `premium`), `student`, `corporate`, `family` and home club (`club-<district>`).
- **Responsibilities.** 10 applications:
  - `class-booking`, `gym-access` and `nutrition-coach`, from the board;
  - `personal-training`, `online-classes`, `spa-access`, `locker-rental`, `body-scan`, `kids-club` and `sauna`.
- **Usages.** About 40 applications enabled per tenant. Configuration: `price`, `currency`, `billingPeriod` and `trialDays`.
- **Executions.** About 420: one per application assigned to one member. Configuration: `plan`, `startDate` and an optional `discount`.
- **Units and units of execution.** Not used, as the board says. No appearance is set for `unt` and `uxe`.
- **Template.** `exe`: `autoRenew: true`, `gracePeriodDays: 7` and `notifications`.
- **Schemas.** The `usg` type schema requires `price` and `currency`. The `exe` type schema requires `plan` (an enum per plan).
- **Lifecycle.**
  - Scheduled assignments that start next month.
  - Expired memberships.
  - A disabled assignment (a frozen membership).
  - A member in Deleted with their assignments, after a data-erasure request.
- **Histories.** A tenant's price list (a usage) has 16 versions.
- **Views.**
  - `staging` follows the board: `fitfactory` with `test-member1` and `test-member2`; `gym-access` and `nutrition-coach`; three assignments. It adds a few test members.
  - `development` has one tenant and five test members.
  - `release-candidate` has two tenants with the new prices.
  - `feature-delivery1` adds the `ai-coach` application for one tenant.

#### 9.3 `fabrikam/invoicing-suite`: the B2B SaaS of the documentation (example 3, about 250)

The model is [b2b-saas.md](../../../42for.net/platform/examples/b2b-saas.md), kept exactly:
- Northwind buys invoicing, payments and portal, in `production` and `sandbox`, on the `enterprise` plan. Contoso buys invoicing in `production` on `standard`.
- `unt.invoicing.nightly-export` is a `time` unit.
- Northwind's production unit of execution moves it to 02:00 `Europe/Oslo`.
- The sandbox unit of execution is disabled, so a test tenant does not export real files.
- The documents follow the documentation's table:
  - `rst.invoicing`: base currency, retry policy and the feature list.
  - `sbt.northwind`: legal name, residency and support tier.
  - `usg.northwind.invoicing`: plan, seats and contracted features.
  - `cnt.northwind.production`: region, log level and host name.
  - `exe.northwind.invoicing.production`: the custom tax profile and the production-only limit.
- The descendant-type schema on `rst.invoicing` for `usg` requires `plan`.
- The descendant-type schema on `sbt.northwind` for `cnt` requires `region`.

The board's default frame adds `exe.northwind.payments.production`, `exe.northwind.portal.production`, `exe.northwind.portal.sandbox` and `uxe.northwind.invoicing.sandbox.nightly-export`.

The project extends this to hundreds of annotations:
- 14 more customers on the same plans: `adatum`, `relecloud`, `trey-research`, `wingtip-toys`, `blue-yonder`, `southridge-video`, `humongous-insurance`, `vanarsdel`, `graphic-design-institute`, `school-of-fine-art`, `consolidated-messenger`, `northridge-fitness`, `liberty-delightful` and `munson-pickles`.
- Their environments.
- Modules `dunning`, `credit-notes` and `reporting`.
- More jobs: `dunning.reminder-letters` and `reporting.month-end`.

Other details:
- Template: `uxe`, the default schedule and retry policy of a job.
- Lifecycle: a customer whose contract ends in 30 days (`expiresIn`); an expired pilot environment; a scheduled new customer; a deleted sandbox with its deployments.
- `development` follows the board: `northwind` only, `sandbox` and a new `qa` environment, `invoicing` and `portal`, the `qa` deployments and job run.

#### 9.4 `bluefin/money-suite`: the consumer finance suite of the documentation (example 4, about 250)

The model is [b2c-finance.md](../../../42for.net/platform/examples/b2c-finance.md):
- Alex acts as `self`, `bright-forge` and `harbor-studio`. The products are `accounts`, `payments`, `cards` and `investments`.
- The executions follow the table:
  - There is no `exe.alex.cards.harbor-studio`. An unavailable product has no execution.
  - Investments exist only for `self`.
  - `usg.alex.cards` exists, because the entitlement is personal.
- `exe.alex.payments.bright-forge` stores `{ limit: 250000, currency: "NOK", vatInvoice: true, approval: "two-eyes-above-50000" }`. Harbor Studio has a supplier-payment profile.
- The subject holds the customer number, the language and the contact.
- The descendant-type schema on `rst.payments` for `exe` requires `limit` and `currency`.
- The descendant-type schema on `sbt.alex` for `cnt` requires `jurisdiction`. `self` records `person`.

The board adds the batch `unt.accounts.monthly-statement` and `uxe.alex.accounts.bright-forge.monthly-statement`.

The project extends this with about 40 more people, mostly Nordic names:
- Each person has `self`. Some have one or two companies.
- The type schema for `cnt` requires `jurisdiction` for everyone.
- Products `savings` and `insurance`.
- Batches `cards.annual-fee` and `investments.rebalance`.

Lifecycle:
- A card product use that expired.
- A scheduled savings entitlement.
- A disabled payments use during a KYC review.
- A person who closed the account, in Deleted with their legal entities.

`release-candidate` follows the board:
- `alex`, with `bright-forge` and `harbor-studio`, but no `self`.
- `cards` and a new product, `lending`.
- `exe.alex.cards.harbor-studio`, the change that gives Harbor Studio cards.
- `exe.alex.lending.bright-forge`.

#### 9.5 `streamwave/streaming-platform`: the distributed platform of the documentation (example 5, about 320)

The model is [distributed-platform.md](../../../42for.net/platform/examples/distributed-platform.md):
- Markets `eu` and `us`, each with cells `prod` and `canary`.
- Services `playback`, `recommendations`, `search` and `billing`.
- `unt.recommendations.model-refresh` on a schedule. The canary unit of execution has an earlier CRON than production.
- `eu` has no `usg.eu.billing`, because billing is not launched there yet.
- Services have labels (`edge`, `ml`, `money`) and a documentation link to their runbook.
- The documents follow the documentation's table: the codec ladder and feature flags on `rst.playback`; market facts on the subject; the licensed package on the usage; traffic share and tracing rate on the cell; the canary-only flag on `exe.eu.playback.canary`.

The project extends this to 8 markets (`uk`, `nordics`, `dach`, `latam`, `apac` and `mena` in addition) and about 20 services:
- `catalog`, `profiles`, `auth`, `downloads`, `subtitles`, `ads`, `live-events`, `parental-controls`, `notifications`, `drm`, `cdn-steering` and others.
- More pipelines: `search.index-rebuild`, `catalog.ingest` and `ads.forecast`.
- Big markets get a `prod-2` cell. Each market has its own time zone.

Other details:
- Template: `exe`, the default timeout and the flags off.
- Schema: the `exe` type schema types the rollout flags and limits `trafficShare`. One deployment is listed in `nonCompliant`: it violates the limit, to show the non-compliance screens.
- `release-candidate` follows the board: the canary cells only, with `playback` and `recommendations` and the canary model refresh. It extends this to every market's canary cell, with the new flags.
- `playback-14` is the documentation's release view: a copy of production in which `exe.*.playback.canary` has the new codec ladder.
- Lifecycle: a market that launches next quarter (scheduled), a retired cell (expired), a disabled deployment and a deleted service with its pipelines.

#### 9.6 `ledgerline/erp`: the versioned monolith of the documentation (example 6, about 200)

The model is [versioned-monolith.md](../../../42for.net/platform/examples/versioned-monolith.md):
- `v2025` is long-term support: label `lts`, `expiresIn` at the end of support. `v2026` is `current`.
- Channels: `v2025.production`, `v2025.contoso` (a contractual pin) and `v2026.production`.
- `reporting` exists only in `v2026`.
- `exe.v2025.billing.contoso` is disabled for the week the pin is frozen.
- The documents follow the documentation's table:
  - `rst.billing`: `vat: true`, `creditNote: true`.
  - `sbt.v2025`: `schemaVersion: 14`. `sbt.v2026`: `schemaVersion: 18`.
  - `usg.v2025.billing`: `creditNote: false`. `usg.v2026.billing`: `creditNote: true`.
  - `cnt.v2025.contoso`: `maintenanceWindow: "sun 01:00-03:00 Europe/Oslo"`.
  - `exe.v2025.billing.contoso`: `legacyExport: true`.

The board adds `unt.billing.month-end-close` and its job run on `v2026.production`.

The project extends this with:
- `v2024`, out of support (expired), and `v2027`, a scheduled preview.
- Pinned customer channels: `northwind`, `woodgrove` and `alpine`.
- Modules `inventory`, `hr`, `crm`, `document-archive`, `audit-log` and `warehouse`.

Other details:
- Template: `exe`, the maintenance defaults.
- Schema: the `sbt` type schema requires `schemaVersion`.
- `staging` follows the board: `v2025` with the `contoso` channel, `billing`, and the backport `usg.v2025.reporting` with `exe.v2025.reporting.contoso`.
- Deleted: a lifted pin, `cnt.v2024.northwind` with its rollouts, as the documentation describes.

#### 9.7 `aurora-motors/vehicle-platform`: the physical product of the documentation (example 7, about 220)

The model is [physical-product.md](../../../42for.net/platform/examples/physical-product.md):
- `aurora`, with trims `touring` and `gt` and the VIN context `vin-4F2`.
- Subsystems `battery-pack`, `infotainment`, `adas` and `brakes`.
- ADAS only on GT.
- `vin-4F2` has its own battery execution with the replacement part and date. It is a sibling of the trims, so it does not inherit the GT execution.
- `unt.infotainment.firmware` has `unitType: "file"` and an artifact definition. GT's firmware pin is `stable`.
- `exe.aurora.battery-pack.gt` stores `{ chemistry: "nmc", capacityKwh: 91, supplier: "helios", homologation: ["eu", "uk"] }`.
- The descendant-type schema on `rst.battery-pack` for `exe` requires `chemistry` and `capacityKwh`, and restricts `chemistry` to the chemistries the factory can build.

The board adds `exe.aurora.infotainment.touring` and `exe.aurora.infotainment.gt` with their firmware pins. Brakes have no executions in the slice.

The project extends this with:
- Models `borealis` (SUV) and `zephyr` (compact), with their trims and a few VIN contexts.
- Subsystems `thermal`, `charging`, `suspension`, `lighting` and `seats`.
- Firmware units `charging.firmware` and `adas.perception-model`.

Other details:
- Template: `uxe`, the default firmware channel.
- `feature-delivery1` follows the board, described as "ADAS on the Touring trim": `touring` only, `infotainment` with its pin, and the new `exe.aurora.adas.touring`.
- Lifecycle: a scheduled model-year trim, an expired homologation, a disabled firmware pin and a deleted recalled part.

#### 9.8 `harbor-industrial/plant-fleet`: the device fleet of the documentation (example 8, about 230)

The model is [device-fleet.md](../../../42for.net/platform/examples/device-fleet.md):
- `harbor-plant`, with `compressor-1`, `compressor-2` and `gateway`, and `west-quay`.
- Capabilities `telemetry`, `control-loop` and `maintenance`.
- `unt.control-loop.sample`.
- `exe.harbor-plant.control-loop.compressor-2` is disabled for service.
- `alertChannel` is a site value. `exe.harbor-plant.control-loop.compressor-1` stores `{ samplePeriodMs: 500, pressureMaxBar: 8.4, alertChannel: "@annotation(\"$.alertChannel\", \"Subject\")" }`.
- The descendant-type schema on `rst.control-loop` for `exe` requires `pressureMaxBar` and caps it.
- The unit of execution on compressor 1 overrides the sampling.

The project extends this with:
- Sites `north-dock`, `river-mill` and `east-yard`, with 8-15 devices each (compressors, pumps, chillers, conveyors, boilers, gateways).
- Capabilities `vibration-monitoring`, `energy-metering` and `video`.
- Jobs `maintenance.calibrate` and `telemetry.firmware-update`.
- Values on devices: model, serial, install date and firmware family.

Other details:
- Template: `exe`, the safe defaults.
- `development` follows the board: one `test-rig` device with telemetry and control loop.
- `campaign-march` is a copy of production with a faster `sample` rate on the observed compressors.
- Lifecycle: a decommissioned device (expired), a scheduled new line and a deleted site extension.

#### 9.9 `meridian-health/clinical-integrations`: hospital network integrations (example 9, about 110)

The board's model, extended to a network:
- Hospitals `st-anne`, `riverside` and `northgate`, with departments (`emergency`, `cardiology`, `oncology`, `radiology`, `pediatrics`, …).
- Clinical systems `lab-results`, `imaging` and `e-prescription`, plus `patient-portal` and `billing-claims`.
- Interfaces `lab-results.hl7-feed` (from the board), `imaging.dicom-router` and `patient-portal.fhir-export`.
- Licenses per hospital. Integrations per department.
- Feeds: an interface in one department, with endpoint, message types and acknowledgment mode.

Rules and settings:
- Schemas require `endpoint` and `certificateRef` on integrations (the type schema of `exe`). `certificateRef` is a binding to the vault: `@(certificates.st-anne-lab, primaryVault)`.
- Ada is Reader here: in a regulated domain, changes go through change control.
- Template: `uxe`, the HL7 acknowledgment and retry defaults.
- `staging` follows the board: `st-anne` with `test-ward`, `lab-results` and `e-prescription`, and the test feed.
- Lifecycle: a scheduled new integration, an expired interim feed, a disabled interface during an upgrade, and a deleted department.

#### 9.10 `appforge/white-label-app`: white-label mobile app (example 10, about 120)

AppForge builds one mobile app that retail brands publish under their own name:
- Brands `freshmart` (from the board), `petpal`, `bookbarn`, `greenleaf-pharmacy` and `urban-bakery`.
- Platforms `ios`, `android` and `web` per brand.
- Features `loyalty`, `checkout` and `push` (from the board), plus `store-locator` and `wallet`.
- The campaign `push.weekly-digest`, from the board.
- Feature licenses per brand.
- App features per platform: bundle id, store id, theme and a push key bound to the vault.

Other details:
- Template: `exe`, the theme defaults.
- Schema: the `cnt` type schema requires `bundleId`.
- `development` follows the board: `freshmart` with `android-beta`, `checkout` and the new `scan-and-go`.
- Lifecycle: a scheduled holiday campaign, an expired campaign, a disabled feature on web, and a deleted brand.

#### 9.11 `gatherly/conferences`: a conference series and its editions (example 11, about 110)

Gatherly runs conference series. Editions are contexts, so their lifecycle follows time:
- Series `devconf` (from the board), `data-summit` and `design-week`.
- Editions, for example `prague-2026` and `online-2026` (from the board), `prague-2025` (expired), `berlin-2027` (scheduled).
- Services `ticketing`, `streaming` and `badges` (from the board), plus `call-for-papers`, `sponsorship` and `venue-wifi`.
- Jobs `ticketing.reminder-mail` (from the board) and `call-for-papers.review-reminder`.
- Configuration: ticket tiers and prices, stream ingest, badge printer and the CFP deadline.

Other details:
- Template: `uxe`, the mail sender.
- Schema: the `exe` descendant-type schema on `rst.ticketing` requires `currency` and `tiers`.
- `feature-delivery1` follows the board: `prague-2026` with `ticketing`, the new `matchmaking` service and its `intro-mail` job.
- Lifecycle: past editions (expired), a future edition (scheduled), a disabled streaming service for an in-person-only edition, and a deleted cancelled edition.

#### 9.12 `pixel-forge/live-ops`: game live operations (new, about 120)

A game studio runs the backend of its titles across platforms and regions. Seasonal events show scheduling.

- Subjects (games): `star-drift`, `dungeon-dash` and `kart-blitz`.
- Contexts (platform regions): `steam-eu`, `steam-na`, `ps5-eu`, `ps5-na`, `switch-jp`, `xbox-eu` and `mobile-apac`.
- Responsibilities (backend services): `matchmaking`, `store`, `events`, `anti-cheat`, `leaderboards` and `cloud-saves`.
- Units (live events under `events`): `winter-festival`, `double-xp-weekend`, `spring-sale` and `anniversary`.
- Usages: the service plan per game. Executions: the services per platform region, with queue sizes, region servers and store currency.
- Units of execution (event runs): one live event on one platform region. The windows are set with `startsIn` and `expiresIn`, so past events are expired and coming events are scheduled.
- Template: `uxe`, the reward defaults.
- Schema: the `unt` type schema requires `rewards`.
- `feature-delivery1` adds `crossplay`.
- Deleted: a cancelled event with its runs.

#### 9.13 `velvet-retail/storefronts`: multi-brand storefronts (new, about 120)

A retailer runs several brand storefronts across European markets. Configuration differs by market: currency, VAT, carriers and payment methods.

- Subjects (storefronts): `velvet`, `urban-step` and `kidzone`.
- Contexts (markets): `de-de`, `at-de`, `cs-cz`, `pl-pl`, `fr-fr` and `en-gb`.
- Responsibilities (commerce capabilities): `checkout`, `payment-provider`, `shipping`, `tax-engine`, `search`, `reviews` and `gift-cards`.
- Units (feeds): `search.product-feed`, `payment-provider.settlement-import` and `shipping.tracking-sync`.
- Usages: the contracted capabilities per storefront.
- Executions: a capability in one market: currency, VAT rate, carrier list, payment methods and a provider key bound to the vault.
- Template: `exe`, the checkout defaults.
- Schemas: the `cnt` type schema requires `currency` and `locale`. A descendant-type schema on `rst.tax-engine` for `exe` requires `vatRate`.
- `feature-delivery1` adds `buy-now-pay-later` in two markets.
- Lifecycle: a scheduled market launch (`it-it`), an expired gift-card promotion and a deleted brand market.

#### 9.14 `seabreeze-hotels/guest-platform`: hotel distribution (new, about 100)

A hotel group sells rooms through its own site and online travel agencies. Rates and commissions are configured per hotel and channel.

- Subjects (hotels): `lisbon-marina`, `porto-riverside`, `algarve-cliffs` and `madeira-gardens`.
- Contexts (channels): `direct-web`, `mobile-app`, `booking-com`, `expedia` and `corporate-gds`.
- Responsibilities (modules): `booking-engine`, `rate-manager`, `mobile-key`, `housekeeping`, `upsell` and `loyalty`.
- Units (sync jobs): `rate-manager.rate-push`, `booking-engine.availability-sync` and `loyalty.points-export`.
- Usages: the modules licensed per hotel.
- Executions: a module on one channel, with commission, rate plans, payment terms and a channel API key bound to the vault.
- Template: `exe`, the cancellation policy.
- Schema: the `exe` descendant-type schema on `rst.rate-manager` requires `ratePlans`.
- Lifecycle: the summer season rate plan (scheduled), an expired promotion, a disabled channel during a contract dispute and a deleted hotel wing.

### 10. Tests

- **`test/unit/mock/showcase.test.ts`** (new). It calls `resetDb('showcase')` in `beforeEach` and checks every project:
  - Every key is valid and canonical (`isValidAnnotationKey`, `withRelations` members). Every ancestor of an active annotation is active. Every trashed key's ancestors are active or in the same tree.
  - The `default` sizes are in range: finance-cloud 170-260, fitness-cloud 900-1,150, documentation projects 150-400, new and board projects 60-200. No type stream reaches 1,000.
  - Each `default` view has active, scheduled, expired, disabled and deleted annotations (a tree with descendants), and histories with 1, 2-5 and 15 or more versions.
  - Each project has at least one template and one schema.
  - Every own configuration in every view satisfies `combinedSchema` (`schemaErrors` is empty), except the project's `nonCompliant` keys, which must fail.
  - `sanitizeTypeAppearances(settings.types)` equals `settings.types`. The time zone is valid and the date format is known.
  - Every project registers `default` (`IsDefault`, described as production), `development`, `staging`, `release-candidate` and `feature-delivery1`. Every registered view builds.
  - Ada has a role on every project. She is Reader on exactly one project and at least Contributor on the others. Reader, Contributor, ContributorWithSecrets, Administrator and Owner each appear for Ada and among the other people.
  - `account.AccessMap` equals the map derived from the access points. Every organization and project has an Owner.
  - Two builds give the same keys, titles, labels, values and contents (the clock is fixed with `vi.setSystemTime`).
  - The documentation's keys and values listed in section 9 are present: a table of key → expected own content fragment.
  - The listing handlers work through `mockHandlers`: the annotations, configurations, templates, schemas, views and deleted lists of one project; the restore of a seeded deletion; the resolved configuration with `@annotation("$.alertChannel", "Subject")`.
  - `scenarios` equals `mockDatasets` from `build/mock-dataset.ts`.
- **`test/unit/mock/start-scenario.test.ts`** (new). It tests the precedence of section 2 with `history.replaceState` and the session storage: the URL wins, a launch change resets, a reload keeps edits, and an unknown value warns and gives `showcase`. It also checks that `currentScenario()` still falls back to `default`.
- **`test/unit/build/mock-dataset.test.ts`** (new):
  - It tests `parseDevMockArguments`: the options, the forwarded arguments, the errors.
  - It runs `node build/dev-mock.ts --legacy --large` and `--scenario=nope` as child processes and expects exit code 2 and the message. This catches TypeScript that Node cannot strip, without starting Vite.
- **`test/unit/configuration/...`.** A test of the extended resolver: `@config` with a pointer and with a dotted path, `@annotation` with and without a type, the legacy form and the 422.
- **`test/e2e/showcase.spec.ts`** (new):
  - It runs `openApp(page, path, 'showcase')` on several projects.
  - It checks the custom type names in the annotations grid ("Customer", "Tenant").
  - It checks the five views on the Views page with the `prod` badge.
  - It checks Deleted with a seeded deletion.
  - It checks the read-only `clinical-integrations` project: no Create, and the Modeling lock.
  - It runs axe on one showcase grid.
  - Existing specs and screenshots do not change.
- **Commands.** From `src/Platform/ui.admin/src`: `npm run typecheck`, `npm run lint`, `npm test`, `npm run test:e2e`. Then `npm run dev:mock`, `npm run dev:mock -- --legacy` and `npm run dev:mock -- --large`, checked in one tab.

### 11. Documentation

- `src/Platform/ui.admin/README.md`, Mock mode: the options of `npm run dev:mock`, the start-up precedence, the dataset list, the showcase projects and Ada's roles, and how to add a showcase project. Also the scripts table (`dev:mock`, `dev:mock:legacy`, `dev:mock:large`).
- Root `CLAUDE.md`: the `dev:mock` line names the options.
- The review goes to `docs/Platform/ui.admin/specs/reviews/` after the implementation.

### 12. Delivery

One phase, in commits in this order:
1. the dataset selection, the launcher, the start-up and the Playwright pin;
2. the showcase framework and the routing;
3. the resolver extension;
4. the projects;
5. the tests;
6. the README.

The review covers the phase.

### 13. Out of scope

- Machines, issuers, certificates, key/value pairs and policies for the showcase projects. They start empty.
- Any change to the API, the SDK or the UI outside the mock, except `clearCatalogSnapshots` in `stores/catalog.ts`.
- Renaming the default view.

### 14. Risks

- **Seed cost.** The seed builds the 14 `default` scopes, to place the deletions. This is about 3,600 annotations once per tab and dataset. Other views are built on first use.
- **Session storage.** It holds the database: the trash (about 150 annotations) and the edits. That is well under the quota. `large` keeps its current size.
- **Shared local storage.** The showcase project settings live in `localStorage`, which is shared across tabs. A fresh showcase start in one tab rewrites the settings of the showcase projects only.

### 15. Deviations from the CodeRabbit plan

- **Fourteen projects instead of eleven.**
  - The Miro board names three more examples: a hospital network, a white-label mobile app, and a conference series.
  - The issue asks for at least three new examples beyond them. These are game live operations, multi-brand storefronts and hotel distribution.
  - CodeRabbit's restaurant point of sale is close to the device fleet, so it is not one of the new examples. Its clinical hub becomes the board's hospital example, still the only Reader project.
- **Project appearance through the project settings in `localStorage`, written when the dataset starts fresh.** There is no in-memory baseline layer in `stores/project-settings.ts`. A baseline would need changes in the store, in `settings.vue` ("Reset to built-in" and its disabled state) and in `TypeAppearanceEditor.vue` (the per-type Reset and the changed state), all for the mock only. Writing the settings at a fresh start keeps the UI untouched. Edits survive reloads, and a dataset reset restores the showcase.
- **The database is stored at the start.** It is no longer stored only at the first write. This makes the precedence and the fresh-start detection reliable.
- **The launcher is TypeScript.** It is written in TypeScript and run by Node (`build/dev-mock.ts`) instead of `.mjs`, like the other build scripts, so it is type-checked and tested the same way.
- **The mock binding resolver gains `@config` and `@annotation`.** Without them, the bindings of the documentation (`@annotation("$.alertChannel", "Subject")`) would answer 422 on the Resolved tab.
- **Six registered views where the documentation names one.** `streaming-platform` and `plant-fleet` also register the views their pages name (`playback-14`, `campaign-march`).
- **File locations.** The spec and the review follow the repository's naming (`docs/Platform/ui.admin/specs/YYYY-MM-DD <Title>.md` and `specs/reviews/`), not `mock-showcase-datasets.md`.
