# Full Markdown in Annotation Descriptions

Linear: [424-9](https://linear.app/424fun/issue/424-9/description-should-support-markdown-fully)

## Problem

The description of an annotation is up to 10,000 characters of free text, and the Administration UI (`src/Platform/ui.admin`) offers a Preview of it as Markdown. The preview only knows a small, hand-written subset: paragraphs, line breaks, `code`, **bold**, *emphasis* and http(s) links. Anything else is shown as typed:

- headings (`# Title`);
- bulleted and numbered lists;
- block quotes;
- fenced and indented code blocks;
- tables;
- strikethrough and horizontal rules.

A description that documents an annotation (a runbook, a table of endpoints, a snippet of configuration) therefore cannot be written in the Markdown people already use elsewhere. The description should support Markdown fully, in the same plain text input.

## Current State

- **Renderer:** `app/utils/markdown.ts`, `renderInlineMarkdown(source)`.
  - It HTML-escapes the whole source, splits paragraphs on blank lines, applies four regular expressions (code, bold, emphasis, links) and turns the remaining `\n` into `<br>`.
  - Only `http(s)` links are produced, with `target="_blank" rel="noopener noreferrer"`. A `javascript:` link stays as typed text.
  - The output is safe for `v-html` because everything is escaped first.
- **Component:** `app/components/common/MarkdownText.vue` renders that HTML with `v-html` in a `space-y-2` container and styles inline `code`. Props: `source` and an optional `empty` text.
- **Users of `MarkdownText`:**
  - `components/annotations/AnnotationForm.vue` (lines 172–206): the Description field is a `UTextarea` (autoresize, 3 rows) with **Write** and **Preview** tabs. Preview shows `MarkdownText` with `empty="Nothing to preview."`.
  - `components/configuration/renderers/ControlField.vue` and `ObjectRenderer.vue`: the `description` of a JSON Schema property as `text-xs text-muted` help text (spec [Administration UI for 2S Platform](2026-10-09%20Administration%20UI%20for%202S%20Platform.md), section 11.2: "inline markdown: code, links, emphasis").
- **Tests:** `test/unit/utils/annotations.test.ts`, "renders a safe markdown subset", covers bold, code, escaped `<script>`, an http(s) link and a refused `javascript:` link.
- **Not affected:**
  - The Description column of the annotation grid (`components/annotations/grid/annotation-columns.ts`) shows the raw text truncated to one line. Issue 424-28 removes that column, so it stays as it is.
  - The history (`utils/audit.ts`) only reports that the description changed.
- **Content Security Policy** (`public/staticwebapp.config.json`): `img-src 'self' data: https:`, no inline scripts. Rendered Markdown must not need more.
- **Dependencies:** there is no Markdown library among the direct dependencies. `marked` is installed only transitively (through `@nuxt/ui` → `@tiptap/markdown`) and is not bundled.

## Proposed Changes

### 1. A Markdown library: `markdown-it`

Add `markdown-it` (`^15.0.2`, ships its own TypeScript types) to `dependencies`.

Why `markdown-it`:

- It follows CommonMark and its default preset adds GFM tables and strikethrough.
- With `html: false`, raw HTML in the source is escaped, not passed through. Its `validateLink` hook decides which URLs may become links or images. Together these make the output safe for `v-html` without a separate sanitizer.
- `marked` is smaller, but it passes raw HTML through. The renderer would have to be hardened by hand, or DOMPurify added.

### 2. Renderer (`app/utils/markdown.ts`)

Replace `renderInlineMarkdown` with `renderMarkdown(source: string | undefined | null): string`. It returns `''` for an empty source, as before.

One module-level `MarkdownIt` instance:

```ts
const markdown = new MarkdownIt('default', {
  html: false, // raw HTML is escaped
  linkify: true, // bare http(s) URLs become links
  breaks: true, // a single newline is a line break, as in the textarea and the previous renderer
  typographer: false,
})
markdown.linkify.set({ fuzzyLink: false }) // "example.com" without a scheme stays text
```

- **Allowed URLs** (`markdown.validateLink`): `http:`, `https:` and `mailto:`, and relative URLs without a scheme (`/acme/billing/default/annotations/rst.invoicing`, `#section`). Every other scheme (`javascript:`, `vbscript:`, `data:`, `file:`, …) is refused. markdown-it then leaves the link text as typed, like the previous renderer. Control characters and whitespace before the scheme are ignored when the scheme is read, as browsers do.
- **Links** (`link_open` rule): `target="_blank"` and `rel="noopener noreferrer"`, as before.
- **Images** (`image` rule): `loading="lazy"` and `referrerpolicy="no-referrer"`. Images go through the same `validateLink`. The CSP already allows `https:` images.
- **Tables** (`table_open` / `table_close` rules): wrapped in `<div class="markdown-table">…</div>`. A wide table then scrolls on its own and does not widen the detail panel.
- **Headings** keep their level (`#` → `<h1>`). The styles keep them small (section 3). The axe checks (`wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`) do not include the `heading-order` best practice.

Supported, by the default preset:

- paragraphs and line breaks;
- headings (ATX `#` and Setext);
- emphasis, strong and ~~strikethrough~~;
- inline code, and fenced and indented code blocks (the info string becomes `class="language-…"`; no syntax highlighting);
- block quotes (nested);
- ordered and unordered lists (nested);
- tables with column alignment;
- horizontal rules;
- links, autolinks and images;
- backslash escapes and entities.

Not supported: raw HTML, task-list checkboxes, footnotes and syntax highlighting.

### 3. `MarkdownText.vue`

- Uses `renderMarkdown`. The props stay the same: `source` and `empty`.
- The root gets a `markdown` class. A `<style scoped>` block styles the rendered elements through `:deep()`. The `v-html` content does not carry the scope attribute, and Tailwind's preflight removes the browser defaults for lists, headings and tables.
  - **Rhythm:** `0.5rem` between blocks (it replaces `space-y-2`).
  - **Headings:** `1.25em`, `1.125em`, `1em`, then `0.875em` for levels 4–6. Semibold in `--ui-text-highlighted`, with extra space above unless the heading comes first. `em` sizes scale with the `text-xs` help of schema properties and the `text-sm` preview.
  - **Lists:** `disc` and `decimal` markers, `1.5em` indent, nested lists without the block gap.
  - **Block quotes:** a `0.25rem` left border in `--ui-border-accented`, `0.75em` indent, `--ui-text-muted` text.
  - **Code:** inline code keeps the current look (`--ui-bg-elevated`, rounded, mono). Code blocks get a `--ui-bg-elevated` box with padding, a rounded border and `overflow-x: auto`. The code inside has no extra background.
  - **Tables:** `.markdown-table` scrolls horizontally. The table is collapsed, with `--ui-border` cell borders and padding, and `--ui-bg-elevated` header cells. `text-align` comes from markdown-it's `style` attributes.
  - **Other elements:** horizontal rules are a `--ui-border` line. Images get `max-width: 100%`. Links are `--ui-primary` and underlined, as before.
- The explanation in the `eslint-disable vue/no-v-html` comment is updated: markdown-it escapes HTML, and only allowed URLs become links.

The schema help in `ControlField` and `ObjectRenderer` uses the same component, so JSON Schema `description`s support full Markdown too. Their current content (inline code, emphasis, links) renders as before.

### 4. Description field (`AnnotationForm.vue`)

- The input stays the plain `UTextarea` with **Write** / **Preview** tabs. There is no rich-text editor.
- `UFormField` gets help text under the field: "Markdown: headings, lists, quotes, code blocks, tables and links." The character count stays in `hint`.
- The textarea starts at 4 rows instead of 3, which leaves room for a short list or a code fence. Autoresize stays on.

### 5. Tests

- `test/unit/utils/markdown.test.ts` (new) for `renderMarkdown`, and the subset test in `annotations.test.ts` is moved there:
  - the previous subset still renders: bold, emphasis, inline code, an http(s) link with `target`/`rel`, and a line break;
  - headings, lists (nested), a block quote, a fenced code block with a language, a table with alignment inside `.markdown-table`, strikethrough and a horizontal rule;
  - safety:
    - `<script>` and inline HTML (`<img onerror>`) are escaped;
    - links with `javascript:`, `JaVaScRiPt:`, `vbscript:` and `data:` are not produced, and neither is a `javascript:` image;
    - relative and `mailto:` links are produced;
    - a bare `https://` URL becomes a link, and `example.com` does not;
  - an empty or `null` source returns `''`.
- `test/unit/components/markdown-text.test.ts` (new): `MarkdownText` renders a table and a heading inside its `.markdown` root, and shows `empty` for an empty source.
- `test/e2e/annotations.spec.ts`: the edit test also types a Markdown description (a heading, a list and a table) and checks the Preview tab. There it expects a heading, list items and a table with the accessible roles.

### 6. Documentation

- `docs/42for.net/platform/admin-ui.md`, the detail section: the description is Markdown (headings, lists, quotes, code blocks, tables, links and images) with a Preview tab; raw HTML is not rendered.
- The phase review in `docs/Platform/ui.admin/specs/reviews/`.
