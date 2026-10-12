# Full Markdown in Annotation Descriptions

## Overview

This review covers the whole of the spec [Full Markdown in Annotation Descriptions](../2026-10-13%20Full%20Markdown%20in%20Annotation%20Descriptions.md), delivered in one phase (Linear [424-9](https://linear.app/424fun/issue/424-9/description-should-support-markdown-fully)). The hand-written Markdown subset of the Administration UI is replaced by `markdown-it`. Annotation descriptions, and the JSON Schema help in configuration forms, now render headings, lists, block quotes, code blocks, tables, strikethrough, horizontal rules, links and images. Raw HTML stays escaped, and only `http(s)`, `mailto` and relative URLs become links or images. The description input is still the plain textarea with Write and Preview tabs. It gains a short help line that names the supported Markdown.

## What Was Done

### 1. A Markdown library: `markdown-it`

- `markdown-it` `^15.0.2` was added to `dependencies` in `src/Platform/ui.admin/src/package.json`. It ships its own types, so no `@types` package is needed.
- The lockfile was updated with npm 11, the version CI uses. The local npm 10 would have dropped the `libc` fields of the native optional packages.
- The new packages are `markdown-it`, `argparse`, `linkify-it`, `mdurl`, `punycode.js`, `uc.micro` and a nested `entities@8`.
- In the production build, `MarkdownText` and the library form their own chunk: 98.7 kB minified, about 41 kB gzipped. It is loaded with the screens that use it (the annotation detail and the configuration forms), not with the app shell.

### 2. Renderer (`app/utils/markdown.ts`)

- `renderInlineMarkdown` is replaced by `renderMarkdown(source)`. It returns `''` for an empty source.
- One module-level instance is configured as planned:
  - preset `default`, with `html: false`, `linkify: true`, `breaks: true` and `typographer: false`;
  - `linkify.set({ fuzzyLink: false })`.
  - Bare e-mail addresses still become `mailto:` links, because `fuzzyEmail` keeps its default.
- **`isAllowedUrl(url)`** is exported and used as `markdown.validateLink`.
  - It removes tabs and newlines anywhere in the URL, and leading control characters and whitespace, as browsers do. Then it reads the scheme.
  - URLs without a scheme are allowed, and so are `http`, `https` and `mailto`. Anything else is refused: `javascript`, `vbscript`, `data` (images too), `file`, `ftp` and drive letters like `c:`.
  - markdown-it then leaves a refused link as the typed text.
- **Render rules:**
  - `link_open` sets `target="_blank"` and `rel="noopener noreferrer"`.
  - `image` sets `loading="lazy"` and `referrerpolicy="no-referrer"`, and then calls the default image rule.
  - `table_open` and `table_close` wrap the table in `<div class="markdown-table">`.
- **Deviation:** links no longer carry `class="text-primary underline"`. The link style moved to the component's stylesheet (section 3).
- **Deviation:** fragment-only links (`#top`) also open in a new tab, like every other link. They are rare in descriptions, so no special case was added.

### 3. `MarkdownText.vue`

- Uses `renderMarkdown`. The props did not change. The root has the `markdown` class, and the empty text is shown as before.
- A `<style scoped>` block with `:deep()` selectors implements the styles of the spec:
  - block rhythm and headings;
  - lists (`circle` markers for nested bulleted lists);
  - block quotes and inline code (same look as before);
  - code blocks with horizontal scroll;
  - tables in a scrolling box, with elevated header cells (the alignment comes from markdown-it's inline `style`);
  - horizontal rules and links.
- `overflow-wrap: break-word` was added on the root. A long URL or key then wraps instead of widening the detail panel.
- The `eslint-disable vue/no-v-html` explanation names the new guarantees.
- The schema help in `ControlField` and `ObjectRenderer` did not change, but now renders full Markdown. The visual baselines of the template and schema screens, which show schema help, still match, so the inline content looks as before.

### 4. Description field (`AnnotationForm.vue`)

- `UFormField` has `help="Markdown: headings, lists, quotes, code blocks, tables and links."`. The character count stays in `hint`.
- The textarea starts with 4 rows instead of 3.
- There is no rich-text editor and no change to the Write / Preview behaviour.

### 5. Tests

- **`test/unit/utils/markdown.test.ts`** (new, 15 tests): the previous inline subset; headings (ATX and Setext); nested and numbered lists; nested quotes; fenced and indented code with escaping; an aligned table inside `.markdown-table`; strikethrough and `<hr>`; escaped raw HTML (`<script>`, `<img onerror>`, `<b>`); refused schemes for links and images; relative, fragment and `mailto` links; image attributes; linkify of bare URLs and e-mails, but not of `example.com`; and `isAllowedUrl` directly.
- The old subset test was removed from `test/unit/utils/annotations.test.ts`.
- **`test/unit/components/markdown-text.test.ts`** (new): a heading, a table cell and list items inside the `.markdown` root, and the empty text.
- **`test/e2e/annotations.spec.ts`:** the edit flow now types a Markdown description, opens **Preview**, and checks a heading, two list items and a table cell by role before it saves.
- **Visual baselines:** the four `annotation-detail-*` screenshots were regenerated in the Playwright Linux container (`npm run test:visual:docker -- --update`) for the help line and the taller textarea. The other 36 screenshots matched.
- **Runs:**
  - `npm run lint`, `npm run typecheck` and `npm run build` are clean;
  - `npm test`: 187 tests passed;
  - `test:e2e`: the annotations, configuration and accessibility specs passed on desktop and mobile (37 passed, 1 skipped by design);
  - the visual suite: 40 passed after the update.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`:
  - the Annotation tab: the description is Markdown with a Preview, which elements are supported, raw HTML is shown as text, and which links work;
  - the configuration form: a property's `description` is Markdown help.
- `README.md` of `ui.admin` was not changed: it does not list individual libraries.

### Not done

- The Description column of the grid still shows the raw text. Issue 424-28 removes that column.
- No syntax highlighting, task lists or footnotes, as planned.
