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

The switcher at the top changes the organization, project or view and keeps the screen you are on. A link to an annotation or configuration opens the same screen for anyone with access to that project.

## The catalog

**Annotations** lists the catalog of the view in two modes:

- **Grid:** sortable, resizable columns.
- **Tree:** grouped by subject or by responsibility.

Selecting a row opens the detail beside the list, so the list keeps its scroll position, selection and filter.

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

The **detail** of an annotation has four tabs:

- **Annotation:** the fields as a form, or as JSON or YAML.
- **Configuration:** its own configuration, the effective (merged) document, and the resolved document.
- **Versions:** the history of its configuration.
- **Schema:** the schema that applies to it.

The breadcrumb and **Lineage** show its ancestors in merge order.

The command palette (`Ctrl K` / `⌘ K`) finds annotations, pages, projects and actions.

## Configuration

A configuration is edited:

- as a **form**, generated from the JSON Schema that applies to the annotation;
- or as **JSON** or **YAML**, with schema hints and validation as you type.

Values inherited from ancestors appear as placeholders; **Reset to inherited** removes your own value. A field can also hold a binding (`@sbt.northwind/baseUrl`) instead of a value. The form shows bindings as expressions and does not validate them, because the platform evaluates them first.

Saving sends only your changes. Two situations stop a save:

- **Schema violation:** the server rejected the document. The errors are listed at the fields, and an Owner or Contributor can still save with **Save anyway**.
- **Concurrent change:** someone else changed the same values in the meantime. The conflict dialog shows their version next to yours. You can reload theirs, copy yours, or overwrite.

**Versions** lists the history of a configuration, a template or a schema. Any two versions can be compared side by side, or in one column on small screens. **Compare views** shows the same key in two views.

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
| Settings | Everyone (policy: Administrator) | The machine authentication policy and the CA certificate download. |

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
- **Tablet:** the detail slides in from the right.
- **Desktop:** the sidebar, list and detail sit side by side and can be resized.

It follows the system light or dark theme unless you pick one on the account page.

Accessibility:
- The screens are tested against WCAG 2.1 AA (axe).
- Types and statuses always carry an icon and text, not only a color.
- Reduced motion turns off the graph animation and panel transitions.

## Running it

The application lives in `src/Platform/ui.admin`. For local development:

- `npm run dev:mock` serves it on port 5174 with an in-browser mock API and a fake user. Use `?mock-scenario=large` for a view of 16,000 annotations.
- `dotnet run --project src/Platform/Aspire.Host/src` starts it next to the API, on port 5173.

Deployments are static (Azure Static Web Apps). Each environment provides an `app-config.json` with the API address and the AuthKit domain. See the [README](https://github.com/akobr/mono.me/tree/main/src/Platform/ui.admin) for setup, configuration and the WorkOS checklist.
