# Administration UI

The Administration UI is the browser application for the 2S Platform. Use it to browse and edit the annotation catalog and its configuration, and to manage who and what may use a project. It talks to the same API as the [`sform` CLI](using-the-platform) and the SDKs, with your own account, and shows only what your role allows.

## Signing in and the first project

You sign in through AuthKit. When you sign in for the first time, the UI checks for invitations:

- **You were invited:** the invitations page lists the projects you can join.
- **You were not invited:** onboarding asks for an organization and a first project, with an optional description of the project. You become their Owner.

After that, the address bar always names the context, like GitHub:

```text
/{organization}/{project}/{view}/annotations
```

The switcher at the top changes the organization, project or view and keeps the screen you are on. A link to an annotation or configuration opens the same screen for anyone with access to that project. The sidebar lists the screens of the view under **View · {view}**, so it always names the selected view, `default` included. The `default` view is the main view of the project, the one that serves production, so a purple **prod** badge follows its name in the sidebar, in the switcher and in the switcher's list of views. The project's **Views** page marks it **production**.

The sidebar always lists the pages of the view, project and organization you last worked in. They stay there on project pages (Settings, Members, …), on the organization overview, and on Organizations and Account. Opening a page of another view, project or organization updates the sidebar. A project you have not opened a view of yet shows its `default` view.

The browser remembers this context, and the address `/` opens it again.

The overview of an organization has a card for each of its projects you are a member of: the name, your role, the key and the first two lines of the project's description. Hover the description for the whole text. **New project** there (Owner of the organization) asks for the name and an optional description, and **New organization** on Organizations asks for an optional project description with the first project. A description takes at most 500 characters.

**Annotations**, **Configurations** and **Graph** remember, per view, how you left them. When you leave one of these screens and come back, through the sidebar, the switcher or the command palette, it looks the same as before. The screen keeps:

- the filter, the open annotation or configuration, and its tab;
- on Annotations, the grid or tree mode, the expanded branches, the selected rows, the sort and the scroll position;
- on Configurations, the scroll position;
- on Graph, the layout, the focus, the hidden types, the dragged nodes, and the zoom and pan.

**Modeling** remembers, per view, its last tab, the open template or schema, and the query of the tab: **Show cascaded** on **Deleted**, the compared key and view on **Compare views**, and the **Edit** or **Versions** tab of the open template or schema. Its sidebar entry and its entry in the command palette bring you back there. The switcher keeps the tab you are on, and the tab entries of the command palette and `G` then `T`, `S` or `E` open the tab they name.

Selecting the screen you are already on in the sidebar goes back to its start: the filter, the open annotation or configuration, the mode, the layout and the focus start over, and **Modeling** opens **Schemas**. The other things above stay as they were. The memory lasts until you reload the page or sign out. An address you type or open from a bookmark is shown as it is.

## The catalog

**Annotations** lists the catalog of the view in two modes:

- **Grid:** sortable, resizable columns.
- **Tree:** by subject or by responsibility.

The catalog shows every status: active, disabled, scheduled and expired, each with its badge. The API returns only active annotations unless it is asked for the others, and the admin UI always asks; filter with `is:active` to see what services see. Deleted annotations are on the **Deleted** tab of **Modeling**.

The tree shows only subjects, or only responsibilities, at the root, with every path down to their descendants:

| Perspective | Paths |
| --- | --- |
| By subject | subject › context › execution › unit of execution<br>subject › usage › execution › unit of execution |
| By responsibility | responsibility › unit › unit of execution<br>responsibility › usage › execution › unit of execution |

So an annotation with two parents appears under both of them, for example an execution under its context and under its usage. Checking the box of one of its rows selects the annotation for the bulk actions, so all its rows are checked. A parent missing from the catalog is shown as a "(missing)" row. With a filter, the tree keeps the matching annotations and the rows above them, dimmed.

The **Type** column, here and on **Configurations**, shows the full name of each type, with its icon and color, including the names from **Settings** and your Preferences. On a phone the list shows only the icon, to keep room for the name and key.

Selecting a row opens its detail from the right, over the list. The list keeps its scroll position, selection and filter, and the part you can still see stays usable: selecting another row switches the detail. Two buttons in the detail header change how it opens:

- **Pin** docks the detail beside the list. Drag the border between them to resize it, or focus the border and use `←` / `→`; a double-click restores the default width.
- **Full screen** opens the detail as a page of its own, in place of the list. Closing it brings the list back where you left it.

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

- **Annotation:** the fields as a form, or as JSON or YAML. The form starts with the type icon and the key, with the name in bold: they are part of the key and cannot be edited. Title, description and documentation link have the same limits as the API (200, 10,000 and 2048 characters). The description is Markdown, and its **Preview** tab renders it. You can use headings, lists, block quotes, code blocks, tables, links and images. Raw HTML is shown as text, and only `http(s)`, `mailto` and relative links work. For a unit, the form, JSON, and YAML also edit the unit type and the unit definition. The type menu suggests Time, Message, and Volume, and any other text is saved. A Time definition that does not have 5 or 6 fields shows a hint and can still be saved.
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

**Bulk create** creates many annotations from a list of keys, typed or pasted, one per line. A line that starts with a type code is that key (`rst.invoicing`). A line without a code is a [compact line](using-the-platform#writing-the-catalog), read by its number of parts: `northwind` is a subject, `northwind.invoicing` a usage, `northwind.invoicing.production` an execution, and four parts a unit of execution. Responsibilities, contexts and units need their code.

- Before anything is created, the dialog lists every line with the key it becomes and whether it is **New**, **Exists**, a **Duplicate** or **Invalid**, and counts the missing ancestors. An invalid line (a code with the wrong number of parts, an empty part, more than four parts without a code, or a tab, `/`, `\`, `?` or `#` in a name) blocks the create until you fix it, or until **Remove invalid lines** removes it.
- The missing ancestors are created with the keys, and keys that already exist are skipped, so the same list can be sent again, for example after an error.
- The new annotations get only their key and name: no title and no time zone. Add those, labels and configurations afterwards.
- One run takes at most 1,000 keys, sent 100 per request.
- On a phone, open it from the command palette (**Bulk create annotations**), or from the empty view.

### Deleting and restoring

**Delete** moves the annotation and everything below it in the graph to **Deleted**, with their configuration. The toast after a delete says until when they can be restored and offers **Undo**. The bulk bar deletes the same way.

**Deleted**, a tab of **Modeling**, lists what was deleted, newest first: who deleted it, how many descendants went with it, and when it is purged automatically (after 90 days). **Show cascaded** lists every deleted annotation, with the key whose delete took it.

- **Restore** brings back the annotation with its descendants; **Restore without descendants** brings back only the annotation. Missing or deleted ancestors come back with it.
- **Delete permanently** removes it for good, with its history. Only Administrators and Owners see it, and it asks you to type the key.
- Opening a deleted key shows when and by whom it was deleted, its history, and **Restore**.

Creating an annotation whose key, or an ancestor's key, is deleted asks first: **Restore and update** brings back the deleted annotation with its configuration and history; **Create new** replaces it with an empty one. **Bulk create** asks the same for all its keys at once, and offers **Restore** instead of **Restore and update**, since it writes nothing over them.

The command palette (`Ctrl K` / `⌘ K`) finds annotations, pages, projects and actions, and switches the theme.

## Configuration

**New** on the **Configurations** page (Contributor or a higher role) starts a configuration. It lists the annotations of the view without their own configuration, so without a row on the page; pick one, and **Open editor** opens its configuration. There, **Create empty** creates it from an empty document and **Start from template** from the template of its type, as from the configuration link in the header of the annotation.

A configuration is edited:

- as a **form**, generated from the JSON Schema that applies to the annotation (a property's `description` is the field's help, in Markdown);
- or as **JSON** or **YAML**, with schema hints and validation as you type.

Values inherited from ancestors appear as placeholders; **Reset to inherited** removes your own value. A field can also hold a binding (`@config("/baseUrl")`) instead of a value: **fx** next to the field turns the value into an expression. The form shows bindings as expressions and does not validate them, because the platform evaluates them first. Under the expression, a line says what it reads, for example "Function config: Reads a value of the configuration being resolved", or points at a likely mistake, such as a `/` inside a path or an unquoted argument.

**fx** inside a binding field, and **fx Bindings** above the JSON and YAML editors, open the binding reference: the expression forms (paths, sources, function calls, text with expressions, math), the functions and sources this server has registered (secret sources are marked), and the JSON Logic and JSON-e object bindings. Every entry has examples to copy; in a field, **Use** puts the example into the value.

The JSON and YAML editors help with bindings too:

- Typing `@` in a string value lists the functions, the sources, and the text and math forms. Picking a function such as `config` writes the call with its quotes.
- Inside `@config("…")` the list offers the paths of this configuration; inside `@annotation("…")` the fields of the annotation's values, or of the ancestor named by the second argument; the second argument offers the ancestor types.
- Hovering a binding explains it, the same way as the line under a form field.

The header has the breadcrumb of the annotation detail, always by subject: the parents of the annotation up to its subject. **Lineage**, at its right, lists every ancestor in merge order, the order in which their configuration is inherited. Each ancestor, in the breadcrumb or in **Lineage**, opens its own configuration.

**Effective** shows the merged document: the ancestors, the template of the type and the own document, read-only, as JSON or YAML. Its **Composition** view explains it like a blame view. The list above the document has every merge in the order applied: each ancestor's template and configuration, indented by its depth and listed before the ancestor that inherits it (marked `via <key>` when that is not the annotation itself), with its version and the number of values it set. A shared ancestor, such as the subject of an execution, appears once per branch and is marked **Applied again**. The gutter of the document shows, on each line, the step that set it, and hovering it names the document. Selecting a step highlights its lines; its paths are listed under it too. An ancestor opens its own configuration, a deleted one is marked **Deleted**, and a template opens in **Modeling** for Contributors and the roles above. A value that a later document repeats keeps the document that set it first. The view loads when you open it, and its address keeps it (`?show=composition`). When the effective document and the composition were read at different moments, a warning offers **Refresh**.

Saving sends only your changes. Two situations stop a save:

- **Schema violation:** the server rejected the document. The errors are listed at the fields, and an Owner or Contributor can still save with **Save anyway**.
- **Concurrent change:** someone else changed the same values in the meantime. The conflict dialog shows their version next to yours. You can reload theirs, copy yours, or overwrite.

**Versions** lists the history of a configuration, a template or a schema. Any two versions can be compared side by side, or in one column on small screens. **Compare views**, a tab of **Modeling**, shows the same key in two views. When the project has only one other view, it is already selected. When it has no other view, the tab is disabled, and the page says so when a link opens it. A view is never compared with itself. **Compare** on the **Views** page opens **Compare views** of the default view with the view of its row. On the row of the default view, it is disabled, and its tooltip says why: the other views are compared with this one, or the project has no other view.

**Templates** and **Schemas** are tabs of **Modeling** too. Templates hold the defaults per annotation type. Schemas define the form and the validation, for a type, for an annotation, or for the descendants of an annotation. A schema shows a form preview, and before you save a change it lists the configurations that the change would break.

## Graph

**Graph** draws the catalog as a layered network. Links point from ancestor to descendant, the direction in which configuration flows.

- **Opening the graph:** large views (more than 5,000 annotations) open on the neighbourhood of one annotation. **Show everything** draws the whole view, up to 20,000 annotations.
- **Mouse:**
  - Hover shows a summary, and a click opens the detail.
  - A double-click focuses on an annotation; drag pins a node.
  - The filter box adds matches to the focus, or dims the rest when the whole view is shown.
- **Keyboard:** the arrow keys move between connected annotations, and `Enter` opens one.
- **Toolbar:** Fit, Reheat (runs the layout again), the layered, force and radial layouts, **Export PNG**, and **Open as list**.

## Modeling

**Modeling**, in the sidebar of the view after **Graph**, has five tabs: **Schemas**, **Templates**, **Export & Import**, **Deleted** and **Compare views**. **Export & Import** is a tab of Administrators and Owners only, so Contributors get the other four. Each tab is a page of its own, at `/{organization}/{project}/{view}/modeling/{tab}`. The sidebar entry returns to the tab you left in the view, with its open template or schema and its query. Modeling opens **Schemas** the first time, when you select its entry while it is open, and from an address `…/modeling` you type or open from a bookmark. The browser's **Back** returns to the previous tab, and an unsaved template or schema asks before you leave it. **Compare views** is disabled when the project has no other view.

The switcher keeps the tab when it changes the view or the project. For Contributors and the roles above, the command palette lists the tabs of your role after **Modeling**, and `G` then `T`, `S` or `E` opens **Templates**, **Schemas** or **Export & Import**. Links to the old addresses of the five pages still work: `…/{view}/schemas/type/exe`, for example, opens `…/{view}/modeling/schemas/type/exe`, and the query and the hash stay.

Modeling needs the Contributor role or a higher one. A Reader sees the entry with a lock, and the page shows the notice "Contributors only" instead of the tabs. In the collapsed sidebar, the tooltip of the entry says the same. So a Reader cannot browse the schemas, the templates or the deleted annotations, export the view or compare views in the admin UI. For the same reason, the **Views** page shows **Compare** only to Contributors and the roles above, and the schema layers on the **Schema** tab of a configuration are plain text for a Reader. This lock is a choice of the admin UI: the API and `sform` still let a Reader read the schemas, the templates and the deleted annotations, and compare views. The export and the import are different: they need the Administrator role in the API and in `sform` too (see [Export and import](#export-and-import)).

## Export and import

**Export & Import**, a tab of **Modeling** (or `G` then `E`), moves the documents of a view as one zip: its annotations, schemas, templates and configurations. The page has an **Export** and an **Import** section, side by side, or one below the other on a phone. Below them is the result of the view's latest import. It needs the Administrator role or the Owner role on the project. A Contributor who opens it, with a link or `G` then `E`, sees the notice "Administrators only" under the other tabs of **Modeling**.

The **Export** section:

- Pick the kinds, all four by default, and the format:
  - **Folder of JSON files**: one folder per kind (`annotations/`, `schemas/`, `templates/`, `configurations/`) with the current documents, the earlier versions as version files, and `export.json`.
  - **Git repository**: the same folders checked out, and every version of a schema, template or configuration a commit by its author.
- The server builds the zip, and the browser downloads it when it is ready, named after the organization, project and view, with the date and the time in UTC. You can leave the page in the meantime: the download still starts, and a toast confirms it with the counts per kind.
- Never included: secrets, effective documents, the audit, the version history of annotations, and deleted annotations.

The **Import** section:

- Drop a zip, for example an edited export or a zipped clone of the repository. Each file's name tells its kind, so folders don't matter: `<key>.json` is an annotation, `<key>.conf.json` a configuration, `<type>.template.json` a template, and a name that ends in `.schema.json` a schema. Version files, `export.json` and hidden folders are ignored.
- Pick the kinds the import may write, all four by default.
- **Existing documents:**
  - **Overwrite** (the default): the archive's document replaces the stored one as a new version, an annotation is updated, and a deleted annotation is re-created with an empty configuration.
  - **Skip**: what the view has stays as it is, and only new documents are written.

  An equal document is left alone either way.
- **Store schemas and configurations that break each other** writes them with a warning. Without it, they fail, and the rest is imported.
- **Check** shows the outcome of every file without writing anything; **Import now** in its result then imports the same file with the same options. **Import** imports at once. Leaving the page doesn't stop an import: a toast reports its end, with a link back to the result.

An import writes the annotations first, ancestors before their descendants, then the schemas, templates and configurations. It checks a schema against the configurations the archive brings, so an archive can change both together. It never deletes what the archive lacks, and it never replays versions: only the current document of each file is imported.

**The result** counts the documents per kind and in total, with the files left out by the kinds and the ignored files. Chips filter the documents by kind, and by **Failed**, **Skipped** and **Warnings**. Every document that was not unchanged is listed with its kind, outcome, version, code, message and warnings, and links to its page when the view has it. On a phone, the table becomes a list. After a reload, the page shows the view's latest import again.

The CLI does the same with `sform story export` and `sform story import` (see [Configuration](configuration#export-and-import)). The API and the CLI follow the same rule: a user needs the Administrator role, and a machine, which has no role, the management scope `Configuration.Transfer` and, for each kind it includes, the read scope to export it or the scopes of writing it directly to import it:

| Kind | Export | Import |
| --- | --- | --- |
| Annotations | `Annotation.Read` | `Annotation.Write` and `Annotation.Manage` |
| Schemas | `Configuration.Read` | `Configuration.Write` and `Schema.Write` |
| Templates | `Configuration.Read` | `Configuration.Write` and `Template.Write` |
| Configurations | `Configuration.Read` | `Configuration.Write` |

## People, machines and settings

| Screen | Who | What |
| --- | --- | --- |
| Members | Administrator | Roles of the organization and of each project. Organization and project roles are independent. Only Owners grant Owner, and the last Owner stays. **Invite** in the top bar invites several people at once: type their addresses with spaces or commas between them, or paste a list (also one copied from a mail client). Everyone gets the same role and expiry. Below the members, **Invitations**: resend, revoke or copy the link. |
| Machines | Contributor (machines with secrets scopes: ContributorWithSecrets; with management scopes, shared certificates and trusted issuers: Administrator) | Credentials for services: runtime and management scopes, an optional annotation restriction (the machine then reaches only that annotation and its descendants), the credential kind and certificate lifetime (or, for a federated machine, its federation rules), reset and delete. Below the machines, **Shared certificates**: issue, revoke, expiry warnings; and **Trusted issuers**: the platforms whose tokens federated machines exchange. |
| Settings | Everyone (description and policies: Administrator, time zone, date format and types: Contributor) | The description of the project, the default time zone of new annotations, the date format, the names, colors and icons of the annotation types, the machine authentication policy, the CA certificate download and, in the **Secrets** section, how the key values of the project are protected. |
| Monitor | Administrator | The **Usage** of the key values (totals, estimated charges, a daily chart) and the **Audit** trail of secrets, one tab each. |

The **Project** card on Settings shows the description of the project to every member, or **No description**. Administrators edit it there instead: **Save description** stores the text trimmed, and saving an empty text removes the description. The organization overview shows the same text.

**Machine authentication** on Settings (Administrator) enables the credential kinds the project's machines may use: API keys, client certificates (mTLS), both together, client credentials of the identity provider, or federated tokens. It also sets the default kind and the default certificate lifetime of new machines. **New machine** on Machines preselects both, and lets you pick any other enabled kind and lifetime. Machines keep the kind they were created with; disabling a kind stops every machine that uses it until it is enabled again. The machine list and detail name the kind in words (`API key`, `Certificate and API key`, `Federated`), and the detail shows the certificate lifetime and when the certificate expires.

**New machine** gives a machine its scopes, in two groups. **Runtime scopes** read and write single documents. **Management scopes** add bulk and structural work, on top of the runtime scope of the operation:

| Scope | Group | Opens |
| --- | --- | --- |
| `Annotation.Read` | Runtime | Reading annotations, their history and the deleted ones |
| `Annotation.Write` | Runtime | Writing annotations one at a time |
| `Configuration.Read` | Runtime | Reading configurations, schemas, templates, key values and the binding catalog |
| `Configuration.Write` | Runtime | Writing and deleting configurations and key values |
| `Secrets.Read` | Runtime | Receiving the values of secrets: in resolved configurations, revealed key values and exported key values, with `Configuration.Read` |
| `Secrets.Write` | Runtime | Writing secret key values, with `Configuration.Write` |
| `Configuration.Transfer` | Management | Exporting and importing a view, and exporting and importing key values, with the scopes of what they include (an import of a view also needs the management scopes below for its schemas, templates and annotations) |
| `Schema.Write` | Management | Writing and deleting schemas, and importing them into a view, with `Configuration.Write` |
| `Template.Write` | Management | Writing and deleting templates, and importing them into a view, with `Configuration.Write` |
| `Annotation.Manage` | Management | Creating many annotations at once, deleting them and restoring them, and importing them into a view, with `Annotation.Write` |
| `KeyValue.Manage` | Management | Protecting key values and restoring many at once, with `Configuration.Write` (protecting also needs `Secrets.Write`) |

Either read scope also lists the views of the project. When the project delivers secrets only over mTLS, the two secrets scopes work only for calls with the machine's client certificate.

**Presets** above the groups fill both of them for a typical machine. The dialog opens with **Runtime reader**, and the boxes can still be changed after a preset. The machine keeps its scopes, not the preset.

| Preset | Scopes |
| --- | --- |
| Runtime reader | `Annotation.Read`, `Configuration.Read` |
| Runtime with secrets | `Annotation.Read`, `Configuration.Read`, `Secrets.Read` |
| CI writer | `Annotation.Read`, `Annotation.Write`, `Configuration.Read`, `Configuration.Write` |
| Git sync | The scopes of CI writer, `Configuration.Transfer`, `Schema.Write`, `Template.Write` and `Annotation.Manage`: it imports every kind |
| Backup | The scopes of Runtime reader and `Configuration.Transfer` |
| Schema deployer | `Configuration.Read`, `Configuration.Write`, `Schema.Write`, `Template.Write` |

Picking a write scope also picks its read scope, and clearing a read scope clears its write scope. **Create** needs at least one runtime scope. Not everyone gives every scope:

- Only Administrators give management scopes; below Administrator the group is disabled.
- Only users with secret rights (ContributorWithSecrets or a higher role) give `Secrets.Read` and `Secrets.Write`; below that role, both are disabled.
- A preset with a scope you cannot give is disabled too.

Give management scopes only to machines that need them, such as a pipeline that syncs a view with a git repository (**Git sync**). Resetting a machine, editing its rules or deleting it needs the role its scopes need: ContributorWithSecrets for a secrets scope, Administrator for a management scope. Below that role, such a machine has no **Reset**, no **Edit rules** and no delete, as deleting it would stop whatever runs with its scopes. The machine list shows a badge per scope, outlined for runtime scopes and shaded for management scopes, and the detail lists the **Runtime scopes** and the **Management scopes** with what each opens, or **None**.

::: warning
Machines created before these scopes existed have none: the API refuses their API keys and certificates with `401` and their client credentials with `403`, and a federated machine gets no token. Their detail shows **None** in both groups. A machine's scopes cannot be edited: create a new machine with the scopes it needs, switch the service or pipeline to it, and delete the old one.
:::

**Federated machines** have no secret. A workload that already has a token of its platform (GitHub Actions, Kubernetes, Vercel, an Azure managed identity, AWS or Google Cloud) exchanges it at the project's token endpoint for a short-lived Storyteller token. Trust comes in two parts:

- **Trusted issuers** (Administrator), a section of Machines: the issuers whose tokens the project accepts, with where their keys come from (discovery, a JWKS URI or a pasted JWKS), the longest token lifetime and the claims every token must carry. **Add trusted issuer** offers a template per platform (GitHub Actions, Kubernetes, Vercel, Azure managed identity, AWS, Google Cloud, Custom) that fills the issuer, the lifetime and the required claims, and tells what to fill in, such as your GitHub organization id. Replace the `<placeholders>` of a template before you add it. The issuer cannot change once it is added; add a new trusted issuer instead. Deleting one stops its tokens within a minute, even while machines still use it, and its name stays taken until those machines' rules change. A project has at most 20 trusted issuers.
- **Federation rules** (Contributor), set with **New machine** and the **Federated** kind: per rule a trusted issuer, the audience, the subject, and further claim pins. The hints under the fields tell what each platform puts there. **Create** stays disabled until every rule is complete. Without a trusted issuer, Administrators get a link to the section, and Contributors are told to ask one. The machine detail lists the rules; **Edit rules** replaces them, and **Setup** shows the setup again.

The audience of a new rule is preset to the project's token endpoint, built from the address the UI calls the API at. **Edit rules** presets new rules with the audience the machine's other rules use, so one machine keeps one audience. Behind a proxy, or when the API has its own public base URL, the token endpoint the API names can differ from that address. The machine detail and the setup then flag each rule whose audience is not the token endpoint, except rules of an Azure issuer, whose audience is the app registration. A workload must request its platform token with exactly the rule's audience, so either use the flagged audience or change it with **Edit rules**.

The API checks issuers and rules and explains a refusal in the dialog, for example an audience that belongs to another service or a GitHub rule without an organization or repository id.

After **Create**, a federated machine shows its setup instead of credentials: the machine id (`client_id`), the token endpoint and a `curl` token exchange that names the audience to request, one per trusted issuer when the rules differ. A machine with a rule of a GitHub Actions issuer also gets a GitHub Actions step with that rule's audience. A federated machine has no **Reset**: change its rules instead. [Authentication](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-access-with-workload-identity-federation) describes each platform's setup.

The **default time zone** is `UTC` until you change it. It is kept in your browser, so other people and other browsers keep their own until the API stores project settings.

Every date in the UI has the same format: `yyyy-mm-dd` (`2026-10-13`) until someone picks another one under **Date format** on Settings. The other formats are `dd.mm.yyyy`, `dd/mm/yyyy`, `mm/dd/yyyy` and `d mmm yyyy` (`13 Oct 2026`). Your own **Date format** on the account page wins over the project's, in every project. **Project default** follows each project again.

- The format applies to the dates in lists, tooltips, banners and messages. A date with a time adds the time on the 24-hour clock and the zone (`2026-10-13 15:04 UTC`).
- The date fields of annotations and configurations show their parts in the same order.
- Like the time zone, the project's format is kept in your browser until the API stores project settings.
- Relative times ("3 hours ago") and the values in documents and in the API (ISO 8601) do not change.

**Annotation types** on Settings gives each of the seven types a name (and its plural), a color and an icon for the whole project: chips, the legend of the graph, lists, filters and dialogs all use them. Your own **Annotation types** on the account page win over the project's, in every project, field by field. A field you leave empty uses the project's value, and then the built-in one.

- Like the time zone, the project's values are kept in your browser until the API stores project settings.
- The colors come from a palette that keeps WCAG AA contrast in both themes, and the icons from a fixed set.
- Only the way a type is shown changes. Keys keep their codes (`exe`), and the API, the documents and the filter (`type:exe`, `type:execution`) keep the type names. The type chips on **Schemas** keep showing the codes.

New credentials (API keys, certificates with their password, client secrets) are shown **once**. The dialog stays open until you confirm that you stored them. Copies to the clipboard are announced, and the UI never keeps the credentials. Federated machines have no credentials, so their setup dialog closes like any other and can be opened again from the machine detail.

## Keyboard

| Keys | Action |
| --- | --- |
| `Ctrl K` / `⌘ K` | Command palette |
| `/` | Focus the filter |
| `[` | Collapse or expand the sidebar |
| `G` then `A`, `C`, `G`, `T`, `S`, `E`, `O` | Annotations, Configurations, Graph, the Templates, Schemas and Export & Import tabs of Modeling, Organizations |
| `J` / `K`, `Enter`, `Esc` | Next / previous row, open, close the detail |
| `Ctrl S` / `⌘ S` | Save |
| `?` | All shortcuts |

## Phones, themes and accessibility

The UI adapts to the screen:

- **Phone:** the lists become cards, the detail opens full-screen, and the filters move to a bottom sheet.
- **Tablet:** the detail slides in from the right, or opens in full screen.
- **Desktop:** the resizable sidebar sits beside the list; the detail opens over the list, pinned beside it and resizable, or in full screen.

It follows the system light or dark theme unless you pick one in the command palette or on the account page. The account page also keeps your other preferences: row density, editor formats, the date format and your own names, colors and icons of the annotation types.

Buttons, links, the selected item and the focus use the purple of the 42 logo: a deeper shade in the light theme and a lighter one in the dark theme, so text in it keeps WCAG AA contrast in both.

Accessibility:
- The screens are tested against WCAG 2.1 AA (axe), in the light and the dark theme.
- Types and statuses always carry an icon and text, not only a color. That holds for renamed and recolored types too.
- Reduced motion turns off the graph animation and panel transitions.

## Running it

The application lives in `src/Platform/ui.admin`. For local development:

- `npm run dev:mock` serves it on port 5174 with an in-browser mock API and a fake user. Use `?mock-scenario=large` for a view of 16,000 annotations.
- `dotnet run --project src/Platform/Aspire.Host/src` starts it next to the API, on port 5173.

An environment can also turn on error reporting (Sentry) and usage analytics (PostHog). Both know you only by your user id, never by your e-mail or name, and see a page as its template, such as `/:org/:project/:view/key-values/:key`, without the query: sign-in codes, invitation tokens and filter text stay in the browser. Analytics counts pages and clicks on links and buttons, with their text, such as the name of the organization, project or key you clicked, but nothing inside the dialogs and screens that show secrets, documents or people; it sets no cookie and honours Do Not Track. Error reports list the API calls before the error, whose paths name the organization, project, view and key, but never contain the body of an API response. Local development and the mock mode send nothing.

Deployments are static (Azure Static Web Apps). Each environment provides an `app-config.json` with the API address and the AuthKit domain. See the [README](https://github.com/akobr/mono.me/tree/main/src/Platform/ui.admin) for setup, configuration and the WorkOS checklist.
