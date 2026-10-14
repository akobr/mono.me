import { EditorState, RangeSetBuilder, type Extension, type Line } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  GutterMarker,
  gutter,
  hoverTooltip,
  ViewPlugin,
  type DecorationSet,
  type ViewUpdate,
} from '@codemirror/view'
import {
  autocompletion,
  snippet,
  startCompletion,
  type Completion,
  type CompletionContext,
  type CompletionResult,
} from '@codemirror/autocomplete'
import type { BindingExample, BindingFunctionDescriptor } from '@42for.net/2splatform.sdk'
import { staticBindingAssist, type BindingAssist } from '@/composables/useBindingAssist'
import {
  bindingCompletionContext,
  builtInEnvelopes,
  builtInExpressions,
  explainBinding,
  quoteFor,
  quotedArguments,
  type BindingCatalogView,
} from '@/utils/binding-language'

// Highlights binding expressions (spec section 9.3): strings starting with "@" (JSON "…", YAML '…' or plain scalars)
// and the envelope keys get a subtle background and an "fx" gutter marker. Inside a binding string the editor
// completes functions, sources and the paths they read, and hovering explains the expression (424-10).

const pattern =
  /"@(?:[^"\\\n]|\\.)*"|'@(?:[^'\n]|'')*'|(?<=:\s|-\s)@[^\s#,}\]]+|"?\$(?:binding|definition|context|jsonlogic|jsone)"?(?=\s*:)/g
const envelopeKey = /"?\$(binding|definition|context|jsonlogic|jsone)"?(?=\s*:)/g

const mark = Decoration.mark({ class: 'cm-binding' })

class FxMarker extends GutterMarker {
  override toDOM() {
    const element = document.createElement('span')
    element.textContent = 'fx'
    element.className = 'cm-binding-gutter'
    element.title = 'Binding expression, evaluated by the server'
    return element
  }
}

const fx = new FxMarker()

function findBindings(view: EditorView): { decorations: DecorationSet; lines: Set<number> } {
  const builder = new RangeSetBuilder<Decoration>()
  const lines = new Set<number>()

  for (const { from, to } of view.visibleRanges) {
    const text = view.state.doc.sliceString(from, to)
    for (const match of text.matchAll(pattern)) {
      const start = from + match.index
      builder.add(start, start + match[0].length, mark)
      lines.add(view.state.doc.lineAt(start).from)
    }
  }

  return { decorations: builder.finish(), lines }
}

const bindingPlugin = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet
    lines: Set<number>

    constructor(view: EditorView) {
      ;({ decorations: this.decorations, lines: this.lines } = findBindings(view))
    }

    update(update: ViewUpdate) {
      if (update.docChanged || update.viewportChanged) {
        ;({ decorations: this.decorations, lines: this.lines } = findBindings(update.view))
      }
    }
  },
  { decorations: (plugin) => plugin.decorations },
)

const bindingGutter = gutter({
  class: 'cm-binding-gutters',
  lineMarker: (view, line) => (view.plugin(bindingPlugin)?.lines.has(line.from) ? fx : null),
  lineMarkerChange: (update) => update.docChanged || update.viewportChanged,
})

/** A string value on one line: the content range without the quotes, and how it is quoted. */
export interface StringValue {
  from: number
  to: number
  delimiter: '"' | "'" | ''
  raw: string
}

/**
 * The string around `pos`: a JSON or YAML double-quoted string, a YAML single-quoted string, or a YAML plain scalar
 * after `: ` or `- `. An unclosed string runs to the end of the line, so typing at its end still counts.
 */
export function stringAt(line: Line, pos: number): StringValue | undefined {
  const text = line.text
  const offset = pos - line.from
  let index = 0

  while (index < text.length) {
    const delimiter = text[index]
    if (delimiter !== '"' && delimiter !== "'") {
      index++
      continue
    }
    const start = index + 1
    let end = start
    while (end < text.length) {
      if (delimiter === '"' && text[end] === '\\') {
        end += 2
      } else if (text[end] === delimiter && delimiter === "'" && text[end + 1] === "'") {
        end += 2
      } else if (text[end] === delimiter) {
        break
      } else {
        end++
      }
    }
    end = Math.min(end, text.length)
    // A key ("@name": …) is not a value, so it is never a binding.
    if (offset >= start && offset <= end) {
      return /^.\s*:/.test(text.slice(end))
        ? undefined
        : { from: line.from + start, to: line.from + end, delimiter, raw: text.slice(start, end) }
    }
    index = end + 1
  }

  const plain = /(?:^\s*-\s+|:\s+)(@[^#]*?)\s*(?:\s#.*)?$/.exec(text)
  if (plain) {
    const start = plain.index + plain[0].indexOf(plain[1]!)
    const end = start + plain[1]!.length
    if (offset >= start && offset <= end) {
      return { from: line.from + start, to: line.from + end, delimiter: '', raw: plain[1]! }
    }
  }

  return undefined
}

/** The text of the string as the server stores it (JSON and YAML double-quoted escapes, YAML '' for '). */
export function decodeString(raw: string, delimiter: StringValue['delimiter']): string {
  if (delimiter === '"') {
    try {
      return JSON.parse(`"${raw}"`) as string
    } catch {
      return raw.replace(/\\(.)/g, '$1')
    }
  }
  return delimiter === "'" ? raw.replaceAll("''", "'") : raw
}

const field = (name: string) => '${' + name + '}'

/** Inline Markdown as DOM: `code` spans, and links shown as their text. */
function inline(parent: HTMLElement, text: string): void {
  text
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
    .split('`')
    .forEach((part, index) => {
      if (!part) {
        return
      }
      if (index % 2) {
        const code = document.createElement('code')
        code.textContent = part
        parent.append(code)
      } else {
        parent.append(document.createTextNode(part))
      }
    })
}

interface Card {
  title: string
  name?: string
  summary?: string
  syntax?: string
  example?: BindingExample
  problem?: string
  footer?: boolean
}

/** The floating card of a completion option or a hover. */
export function bindingCard(card: Card): HTMLElement {
  const root = document.createElement('div')
  root.className = 'cm-binding-card'
  const add = (className: string, fill: (element: HTMLElement) => void) => {
    const element = document.createElement('div')
    element.className = className
    fill(element)
    root.append(element)
  }

  add('cm-binding-card-title', (element) => {
    const badge = document.createElement('span')
    badge.className = 'cm-binding-card-fx'
    badge.textContent = 'fx'
    element.append(badge, document.createTextNode(` ${card.title}`))
    if (card.name) {
      const code = document.createElement('code')
      code.textContent = card.name
      element.append(document.createTextNode(' '), code)
    }
  })
  if (card.summary) {
    add('cm-binding-card-summary', (element) => inline(element, card.summary!))
  }
  if (card.syntax) {
    add('cm-binding-card-syntax', (element) => (element.textContent = card.syntax!))
  }
  if (card.example) {
    add('cm-binding-card-example', (element) => {
      const code = document.createElement('code')
      code.textContent = card.example!.Expression ?? ''
      element.append(code)
      if (card.example!.Description) {
        const description = document.createElement('div')
        inline(description, card.example!.Description)
        element.append(description)
      }
    })
  }
  if (card.problem) {
    add('cm-binding-card-problem', (element) => inline(element, card.problem!))
  }
  if (card.footer) {
    add('cm-binding-card-footer', (element) => {
      element.textContent = 'Evaluated by the server when the resolved configuration is read.'
    })
  }
  return root
}

/** The call of a function as a snippet: quoted parameters as empty fields, with the quotes of the string. */
export function functionSnippet(entry: BindingFunctionDescriptor, quote: string): string {
  const required = (entry.Parameters ?? []).filter((parameter) => !parameter.IsOptional)
  const args = required.map((parameter) =>
    entry.Signature?.includes(`"<${parameter.Name}>"`) ? `${quote}${field('')}${quote}` : field(parameter.Name ?? ''),
  )
  return `${entry.Name}(${args.length ? args.join(', ') : field('')})`
}

function functionOptions(catalog: BindingCatalogView, quote: string): Completion[] {
  return catalog.functions.map((entry) => {
    const template = snippet(functionSnippet(entry, quote))
    return {
      label: entry.Name ?? '',
      type: 'function',
      detail: entry.Signature ?? 'function',
      info: () =>
        bindingCard({ title: 'Function', name: entry.Name, summary: entry.Summary, example: entry.Examples?.[0] }),
      // The quoted argument is a path the function reads: list the paths right away.
      apply: (view, completion, from, to) => {
        template(view, completion, from, to)
        if (entry.Parameters?.length) {
          startCompletion(view)
        }
      },
      boost: 2,
    }
  })
}

function sourceOptions(catalog: BindingCatalogView, sourced: boolean): Completion[] {
  return catalog.sources.map((entry) => ({
    label: entry.Name ?? '',
    type: 'variable',
    detail: `${entry.IsSecret ? 'secret ' : ''}${entry.IsDefault ? 'default ' : ''}source`,
    info: () =>
      bindingCard({
        title: entry.IsSecret ? 'Secret source' : 'Source',
        name: entry.Name,
        summary: entry.Summary,
        example: entry.Examples?.[0],
      }),
    apply: sourced ? snippet(`(${field('path.to.value')}, ${entry.Name})`) : (entry.Name ?? ''),
    boost: 1,
  }))
}

function formOptions(): Completion[] {
  const interpolation = builtInExpressions.find((entry) => entry.id === 'interpolation')!
  const math = builtInExpressions.find((entry) => entry.id === 'math')!
  return [interpolation, math].map((entry) => ({
    label: entry.id === 'math' ? '{…}' : '[…]',
    displayLabel: entry.syntax.slice(1),
    type: 'keyword',
    detail: entry.title.toLowerCase(),
    info: () => bindingCard({ title: entry.title, summary: entry.summary, example: entry.examples[0] }),
    apply: snippet(entry.id === 'math' ? `{${field('expression')}}` : `[${field('text')}]`),
  }))
}

/** Completion inside binding strings: functions, sources, the interpolation and math forms, paths and types. */
export function bindingCompletions(assist: BindingAssist) {
  return async (context: CompletionContext): Promise<CompletionResult | null> => {
    const found = stringAt(context.state.doc.lineAt(context.pos), context.pos)
    if (!found || !found.raw.startsWith('@')) {
      return null
    }

    const prefix = decodeString(context.state.sliceDoc(found.from, context.pos), found.delimiter)
    const where = bindingCompletionContext(prefix)
    const catalog = assist.catalog.value
    const from = context.pos - (where.kind === 'none' ? 0 : where.word.length)

    switch (where.kind) {
      case 'start':
        return {
          from,
          options: [
            ...functionOptions(catalog, quoteFor(found.delimiter)),
            ...sourceOptions(catalog, true),
            ...(where.nested ? [] : formOptions()),
          ],
          validFor: /^[A-Za-z0-9_]*$/,
        }
      case 'source':
        return { from, options: sourceOptions(catalog, false), validFor: /^[A-Za-z0-9_]*$/ }
      case 'argument': {
        const parameter = catalog.functions.find((entry) => entry.Name === where.fn)?.Parameters?.[where.index]
        let values: string[] = []
        if (where.fn === 'annotation' && where.index === 1) {
          values = assist.annotationTypes()
        } else if (parameter?.Values?.length) {
          values = parameter.Values
        } else if (where.fn === 'config' && where.index === 0) {
          values = assist.configPointers()
        } else if (where.fn === 'annotation' && where.index === 0) {
          const type = quotedArguments(decodeString(found.raw, found.delimiter), 'annotation')[1]
          values = await assist.annotationPointers(type)
        }
        return values.length
          ? {
              from,
              options: values.map((value) => ({ label: value, type: where.index === 0 ? 'property' : 'enum' })),
              validFor: /^[^"'\\]*$/,
            }
          : null
      }
      default:
        return null
    }
  }
}

/** The card of the envelope key under `pos`: the engine named on the same line, or both. */
function envelopeCard(line: Line, pos: number): { from: number; to: number; dom: HTMLElement } | undefined {
  for (const match of line.text.matchAll(envelopeKey)) {
    const from = line.from + match.index
    const to = from + match[0].length
    if (pos < from || pos > to) {
      continue
    }
    const engine = /"\$binding"\s*:\s*"(jlogic|jsone)"/.exec(line.text)?.[1]
    const entries = builtInEnvelopes.filter((entry) => !engine || entry.id === engine)
    const dom = document.createElement('div')
    for (const entry of entries) {
      dom.append(
        bindingCard({ title: entry.title, name: `$${match[1]}`, summary: entry.summary, syntax: entry.syntax }),
      )
    }
    return { from, to, dom }
  }
  return undefined
}

function bindingHover(assist: BindingAssist): Extension {
  return hoverTooltip((view, pos) => {
    const line = view.state.doc.lineAt(pos)
    const found = stringAt(line, pos)

    if (found?.raw.startsWith('@')) {
      const explanation = explainBinding(decodeString(found.raw, found.delimiter), assist.catalog.value)
      if (explanation) {
        return {
          pos: found.from,
          end: found.to,
          above: true,
          create: () => ({ dom: bindingCard({ ...explanation, footer: true }) }),
        }
      }
    }

    const envelope = envelopeCard(line, pos)
    return envelope
      ? { pos: envelope.from, end: envelope.to, above: true, create: () => ({ dom: envelope.dom }) }
      : null
  })
}

const bindingTheme = EditorView.baseTheme({
  '.cm-binding': {
    backgroundColor: 'color-mix(in oklab, var(--ui-info) 14%, transparent)',
    borderRadius: '3px',
  },
  '.cm-binding-gutter': {
    color: 'var(--ui-info)',
    fontSize: '10px',
    fontStyle: 'italic',
    fontWeight: '600',
    padding: '0 3px',
  },
  '.cm-binding-card': {
    maxWidth: '24rem',
    padding: '6px 8px',
    fontFamily: 'var(--font-sans)',
    fontSize: '12px',
    lineHeight: '1.45',
    whiteSpace: 'normal',
  },
  '.cm-binding-card + .cm-binding-card': { borderTop: '1px solid var(--ui-border)' },
  '.cm-binding-card > * + *': { marginTop: '4px' },
  '.cm-binding-card code, .cm-binding-card-syntax': {
    fontFamily: 'var(--font-mono)',
    fontSize: '11px',
    overflowWrap: 'anywhere',
  },
  '.cm-binding-card code': {
    backgroundColor: 'var(--ui-bg-elevated)',
    borderRadius: '3px',
    padding: '0 3px',
  },
  '.cm-binding-card-title': { fontWeight: '600', color: 'var(--ui-text-highlighted)' },
  '.cm-binding-card-fx': { color: 'var(--ui-info)', fontFamily: 'var(--font-mono)', fontStyle: 'italic' },
  '.cm-binding-card-syntax': { color: 'var(--ui-text-muted)' },
  '.cm-binding-card-example > div': { color: 'var(--ui-text-muted)' },
  '.cm-binding-card-problem': { color: 'var(--ui-warning)' },
  '.cm-binding-card-footer': { color: 'var(--ui-text-dimmed)' },
})

/**
 * Binding highlighting, the "fx" gutter, completion and hover. `assist` supplies the catalog and the paths; without
 * it the fallback catalog applies and no paths are offered.
 */
export function bindingDecorations(assist: BindingAssist = staticBindingAssist): Extension {
  // The completion keeps the state of a source by its identity, so the provider returns the same function.
  const completionData = [{ autocomplete: bindingCompletions(assist) }]
  return [
    bindingPlugin,
    bindingGutter,
    bindingTheme,
    bindingHover(assist),
    autocompletion(),
    EditorState.languageData.of(() => completionData),
  ]
}
