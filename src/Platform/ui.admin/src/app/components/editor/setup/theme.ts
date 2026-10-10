import { EditorView } from '@codemirror/view'
import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { tags } from '@lezer/highlight'

/**
 * Editor chrome and syntax colors from CSS variables (Nuxt UI tokens and --cm-* in main.css), so light and dark
 * follow the color mode without recreating the editor.
 */
export const editorTheme = EditorView.theme({
  '&': {
    color: 'var(--ui-text)',
    backgroundColor: 'var(--ui-bg)',
    fontSize: '13px',
    height: '100%',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': { fontFamily: 'var(--font-mono)', lineHeight: '1.55' },
  '.cm-content': { caretColor: 'var(--ui-primary)' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--ui-primary)' },
  '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground, ::selection': {
    backgroundColor: 'color-mix(in oklab, var(--ui-primary) 22%, transparent)',
  },
  '.cm-gutters': {
    backgroundColor: 'var(--ui-bg-muted)',
    color: 'var(--ui-text-dimmed)',
    border: 'none',
    borderRight: '1px solid var(--ui-border)',
  },
  '.cm-activeLine': { backgroundColor: 'color-mix(in oklab, var(--ui-bg-elevated) 60%, transparent)' },
  '.cm-activeLineGutter': { backgroundColor: 'var(--ui-bg-elevated)', color: 'var(--ui-text)' },
  '.cm-foldPlaceholder': { backgroundColor: 'var(--ui-bg-elevated)', border: 'none', color: 'var(--ui-text-muted)' },
  '.cm-tooltip': {
    backgroundColor: 'var(--ui-bg)',
    border: '1px solid var(--ui-border)',
    borderRadius: '6px',
    color: 'var(--ui-text)',
  },
  '.cm-tooltip-autocomplete > ul > li[aria-selected]': {
    backgroundColor: 'var(--ui-bg-elevated)',
    color: 'var(--ui-text-highlighted)',
  },
  '.cm-panels': { backgroundColor: 'var(--ui-bg-muted)', color: 'var(--ui-text)' },
  '.cm-searchMatch': { backgroundColor: 'color-mix(in oklab, var(--ui-warning) 30%, transparent)' },
  '.cm-diagnostic-error': { borderLeftColor: 'var(--ui-error)' },
  '.cm-diagnostic-warning': { borderLeftColor: 'var(--ui-warning)' },
  '.cm-lintRange-error': { backgroundImage: 'none', textDecoration: 'underline wavy var(--ui-error)' },
  '.cm-lintRange-warning': { backgroundImage: 'none', textDecoration: 'underline wavy var(--ui-warning)' },
})

export const editorHighlight = syntaxHighlighting(
  HighlightStyle.define([
    { tag: [tags.propertyName, tags.definition(tags.propertyName)], color: 'var(--cm-property)' },
    { tag: [tags.string, tags.special(tags.string)], color: 'var(--cm-string)' },
    { tag: [tags.number, tags.integer, tags.float], color: 'var(--cm-number)' },
    { tag: [tags.bool, tags.null, tags.atom, tags.keyword], color: 'var(--cm-keyword)' },
    { tag: [tags.comment, tags.lineComment], color: 'var(--cm-comment)', fontStyle: 'italic' },
    { tag: [tags.punctuation, tags.separator, tags.brace, tags.squareBracket], color: 'var(--cm-punctuation)' },
    { tag: [tags.meta, tags.typeName, tags.labelName], color: 'var(--cm-meta)' },
  ]),
)
