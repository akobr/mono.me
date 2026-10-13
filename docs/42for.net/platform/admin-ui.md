# Administration UI

The Administration UI is the browser application for the 2S Platform. Use it to browse and edit the annotation catalog and its configuration, and to manage who and what may use a project. It talks to the same API as the [`sform` CLI](using-the-platform) and the SDKs, with your own account, and shows only what your role allows.

## Signing in and the first project

You sign in through AuthKit. When you sign in for the first time, the UI checks for invitations:

- **You were invited:** the invitations page lists the projects you can join.
- **You were not invited:** onboarding asks for an organization and a first project. You become their Owner.

After that, the address bar always names the context, like GitHub:

```text
/{organization}/{project}/{view}/annotations
```

The switcher at the top changes the organization, project or view and keeps the screen you are on. A link to an annotation or configuration opens the same screen for anyone with access to that project. The sidebar lists the screens of the view under **View · {view}**, so it always names the selected view, `default` included.

The sidebar always lists the pages of the view, project and organization you last worked in. They stay there on project pages (Settings, Members, …), on the organization overview, and on Organizations and Account. Opening a page of another view, project or organization updates the sidebar. A project you have not opened a view of yet shows its `default` view.

The browser remembers this context, and the address `/` opens it again.

## The catalog

**Annotations** lists the catalog of the view in two modes:

- **Grid:** sortable, resizable columns.
- **Tree:** by subject or by responsibility.

The tree shows only subjects, or only responsibilities, at the root, with every path down to their descendants:

| Perspective | Paths |
| --- | --- |
| By subject | subject › context › execution › unit of execution<br>subject › usage › execution › unit of execution |
| By responsibility | responsibility › unit › unit of execution<br>responsibility › usage › execution › unit of execution |

So an annotation with two parents appears under both of them, for example an execution under its context and under its usage. Checking the box of one of its rows selects the annotation for the bulk actions, so all its rows are checked. A parent missing from the catalog is shown as a "(missing)" row. With a filter, the tree keeps the matching annotations and the rows above them, dimmed.

The **Type** column shows the full name of each type, with its icon and color, including the names from **Settings** and your Preferences. On a phone the list shows only the icon, to keep room for the name and key.

Selecting a row opens its detail from the right, over the list. The list keeps its scroll position, selection and filter, and the part you can still see stays usable: selecting another row switches the detail. Two buttons in the detail header change how it opens:

- **Pin** docks the detail beside the list. Drag the border between them to resize it, or focus the border and use `←` / `→`; a double-click restores the default width.
- **Full screen** opens the detail as a page of its own, in place of the list.

The UI remembers your choice and the width, and opens the next detail the same way. Configurations, templates, schemas and machines open their detail the same way.

The filter box takes words and qualifiers:

| Qualifier | Example | Meaning |
| --- | --- | --- |
| `type` | `type:exe,cnt` | Annotation types (codes or names) |
| `label` | `label:eu,us` | Any of the labels |
| `is` | `is:active`, `-is:disabled` | Status: active, disabled, scheduled, expired |
| `has` | `has:config` | Has a configuration, labels, values, docs or a description |
| `key` | `key:exe.*.payments.*` | Key prefix or pattern |
| `subject`, `responsibility`, `context`, `unit` | `subject:northwind` | A part of the key |
| `ancestor` | `ancestor:rst.invoicing` | Everything below an annotation |

Notes on the syntax:
- A comma means *or*, and `-` negates a term.
- Quotes keep spaces.
- Other words search the name, key, title and description.

On a phone, the **Filters** button opens the same filters as toggles in a bottom sheet.

The **detail** of an annotation has two tabs:

- **Annotation:** the fields as a form, or as JSON or YAML. The form starts with the type icon and the key, with the name in bold: they are part of the key and cannot be edited. Title, description and documentation link have the same limits as the API (200, 10,000 and 2048 characters).
- **History:** who changed it and when, newest first. An update lists every field with its old and new value; the description only shows that it changed. Deletes, restores and configuration saves are entries too, and a re-created annotation starts a new lifetime.

The header links to the configuration, drawn by where the configuration comes from:

| Link | Meaning |
| --- | --- |
| **Configuration · v7** | The annotation has its own values (version 7). The tooltip names what it also inherits. |
| **Inherited configuration** | No own values; it inherits from ancestors or type templates. |
| **No configuration** | Nothing applies to it. Contributors can create one from there. |

The configuration itself is edited on its own page (see Configuration). The header also shows when the annotation was created and last updated, and by whom.

The breadcrumb and **Lineage** show its ancestors in merge order.

**New** creates an annotation from its type and names. Its time zone (the zone of **Valid from** and **Expires**) starts as the project's default time zone from **Settings**, and the dialog lets you pick another one. Missing ancestors created with it get the same zone.

### Deleting and restoring

**Delete** moves the annotation and everything below it in the graph to **Deleted**, with their configuration. The toast after a delete says until when they can be restored and offers **Undo**. The bulk bar deletes the same way.

**Deleted** (in the sidebar of the view) lists what was deleted, newest first: who deleted it, how many descendants went with it, and when it is purged automatically (after 90 days). **Show cascaded** lists every deleted annotation, with the key whose delete took it.

- **Restore** brings back the annotation with its descendants; **Restore without descendants** brings back only the annotation. Missing or deleted ancestors come back with it.
- **Delete permanently** removes it for good, with its history. Only Administrators and Owners see it, and it asks you to type the key.
- Opening a deleted key shows when and by whom it was deleted, its history, and **Restore**.

Creating an annotation whose key, or an ancestor's key, is deleted asks first: **Restore and update** brings back the deleted annotation with its configuration and history; **Create new** replaces it with an empty one.

The command palette (`Ctrl K` / `⌘ K`) finds annotations, pages, projects and actions.

## Configuration

A configuration is edited:

- as a **form**, generated from the JSON Schema that applies to the annotation;
- or as **JSON** or **YAML**, with schema hints and validation as you type.

Values inherited from ancestors appear as placeholders; **Reset to inherited** removes your own value. A field can also hold a binding (`@sbt.northwind/baseUrl`) instead of a value. The form shows bindings as expressions and does not validate them, because the platform evaluates them first.

**Lineage** in the header lists the ancestors in merge order, the order in which their configuration is inherited. Each ancestor opens its own configuration.

Saving sends only your changes. Two situations stop a save:

- **Schema violation:** the server rejected the document. The errors are listed at the fields, and an Owner or Contributor can still save with **Save anyway**.
- **Concurrent change:** someone else changed the same values in the meantime. The conflict dialog shows their version next to yours. You can reload theirs, copy yours, or overwrite.

**Versions** lists the history of a configuration, a template or a schema. Any two versions can be compared side by side, or in one column on small screens. **Compare views** shows the same key in two views. When the project has only one other view, it is already selected.

**Templates** hold the defaults per annotation type. **Schemas** define the form and the validation, for a type, for an annotation, or for the descendants of an annotation. A schema shows a form preview, and before you save a change it lists the configurations that the change would break.

## Graph

**Graph** draws the catalog as a layered network. Links point from ancestor to descendant, the direction in which configuration flows.

- **Opening the graph:** large views (more than 5,000 annotations) open on the neighbourhood of one annotation. **Show everything** draws the whole view, up to 20,000 annotations.
- **Mouse:**
  - Hover shows a summary, and a click opens the detail.
  - A double-click focuses on an annotation; drag pins a node.
  - The filter box adds matches to the focus, or dims the rest when the whole view is shown.
- **Keyboard:** the arrow keys move between connected annotations, and `Enter` opens one.
- **Toolbar:** Fit, Reheat (runs the layout again), the layered, force and radial layouts, **Export PNG**, and **Open as list**.

## People, machines and settings

| Screen | Who | What |
| --- | --- | --- |
| Members | Administrator | Roles of the organization and of each project. Organization and project roles are independent. Only Owners grant Owner, and the last Owner stays. |
| Invitations | Administrator | Invite by email with a role and an expiry; resend, revoke or copy the link. |
| Machines | Contributor | Credentials for services: scope, an optional annotation restriction, reset and delete. |
| Certificates | Administrator | Shared client certificates: issue, revoke, expiry warnings. |
| Settings | Everyone (policy: Administrator, time zone, date format and types: Contributor) | The default time zone of new annotations, the date format, the names, colors and icons of the annotation types, the machine authentication policy and the CA certificate download. |

The **default time zone** is `UTC` until you change it. It is kept in your browser, so other people and other browsers keep their own until the API stores project settings.

Every date in the UI has the same format: `yyyy-mm-dd` (`2026-10-13`) until someone picks another one under **Date format** on Settings. The other formats are `dd.mm.yyyy`, `dd/mm/yyyy`, `mm/dd/yyyy` and `d mmm yyyy` (`13 Oct 2026`). Your own **Date format** on the account page wins over the project's, in every project. **Project default** follows each project again.

- The format applies to the dates in lists, tooltips, banners and messages. A date with a time adds the time on the 24-hour clock and the zone (`2026-10-13 15:04 UTC`).
- The date fields of annotations and configurations show their parts in the same order.
- Like the time zone, the project's format is kept in your browser until the API stores project settings.
- Relative times ("3 hours ago") and the values in documents and in the API (ISO 8601) do not change.

**Annotation types** on Settings gives each of the seven types a name (and its plural), a color and an icon for the whole project: chips, the legend of the graph, lists, filters and dialogs all use them. Your own **Annotation types** on the account page win over the project's, in every project, field by field. A field you leave empty uses the project's value, and then the built-in one.

- Like the time zone, the project's values are kept in your browser until the API stores project settings.
- The colors come from a palette that keeps WCAG AA contrast in both themes, and the icons from a fixed set.
- Only the way a type is shown changes. Keys keep their codes (`exe`), and the API, the documents and the filter (`type:exe`, `type:execution`) keep the type names.

New credentials (API keys, certificates with their password, client secrets) are shown **once**. The dialog stays open until you confirm that you stored them. Copies to the clipboard are announced, and the UI never keeps the credentials.

## Keyboard

| Keys | Action |
| --- | --- |
| `Ctrl K` / `⌘ K` | Command palette |
| `/` | Focus the filter |
| `[` | Collapse or expand the sidebar |
| `G` then `A`, `C`, `G`, `T`, `S`, `O` | Annotations, Configurations, Graph, Templates, Schemas, Organizations |
| `J` / `K`, `Enter`, `Esc` | Next / previous row, open, close the detail |
| `Ctrl S` / `⌘ S` | Save |
| `?` | All shortcuts |

## Phones, themes and accessibility

The UI adapts to the screen:

- **Phone:** the lists become cards, the detail opens full-screen, and the filters move to a bottom sheet.
- **Tablet:** the detail slides in from the right, or opens in full screen.
- **Desktop:** the resizable sidebar sits beside the list; the detail opens over the list, pinned beside it and resizable, or in full screen.

It follows the system light or dark theme unless you pick one on the account page. The account page also keeps your other preferences: row density, editor formats, the date format and your own names, colors and icons of the annotation types.

Accessibility:
- The screens are tested against WCAG 2.1 AA (axe).
- Types and statuses always carry an icon and text, not only a color. That holds for renamed and recolored types too.
- Reduced motion turns off the graph animation and panel transitions.

## Running it

The application lives in `src/Platform/ui.admin`. For local development:

- `npm run dev:mock` serves it on port 5174 with an in-browser mock API and a fake user. Use `?mock-scenario=large` for a view of 16,000 annotations.
- `dotnet run --project src/Platform/Aspire.Host/src` starts it next to the API, on port 5173.

Deployments are static (Azure Static Web Apps). Each environment provides an `app-config.json` with the API address and the AuthKit domain. See the [README](https://github.com/akobr/mono.me/tree/main/src/Platform/ui.admin) for setup, configuration and the WorkOS checklist.
