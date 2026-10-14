import { computed } from 'vue'
import { describe, expect, it } from 'vitest'
import { EditorState } from '@codemirror/state'
import { CompletionContext, type CompletionResult } from '@codemirror/autocomplete'
import { json } from '@codemirror/lang-json'
import { yaml } from '@codemirror/lang-yaml'
import type { BindingAssist } from '@/composables/useBindingAssist'
import {
  bindingCard,
  bindingCompletions,
  bindingDecorations,
  decodeString,
  functionSnippet,
  stringAt,
} from '@/components/editor/setup/bindings'
import { catalogView } from '@/utils/binding-language'

const catalog = catalogView({
  Functions: catalogView().functions,
  Sources: [{ Name: 'primaryVault', Summary: 'Secrets.', IsDefault: false, IsSecret: true, Examples: [] }],
})

const requested: Array<string | undefined> = []
const assist: BindingAssist = {
  catalog: computed(() => catalog),
  configPointers: () => ['/host', '/endpoints', '/endpoints/db'],
  annotationPointers: async (type) => {
    requested.push(type)
    return type ? ['/currency'] : ['/owner', '/tier']
  },
  annotationTypes: () => ['Subject', 'Context'],
}

/** Completes at the `|` of `text` in a JSON (or YAML) document with the binding extension. */
async function complete(text: string, language: 'json' | 'yaml' = 'json'): Promise<CompletionResult | null> {
  const pos = text.indexOf('|')
  const state = EditorState.create({
    doc: text.replace('|', ''),
    extensions: [language === 'json' ? json() : yaml(), bindingDecorations(assist)],
  })
  return bindingCompletions(assist)(new CompletionContext(state, pos, false))
}

const labels = (result: CompletionResult | null) => result?.options.map((option) => option.label)

describe('binding completion and hover in the code editors (424-10)', () => {
  it('offers functions, sources and the text and math forms after @', async () => {
    const result = await complete('{ "endpoint": "@|" }')

    expect(labels(result)).toEqual(['annotation', 'config', 'primaryVault', '[…]', '{…}'])
    expect(result?.options.find((option) => option.label === 'config')).toMatchObject({
      type: 'function',
      detail: 'config("<expr>")',
    })
    expect(result?.options.find((option) => option.label === 'primaryVault')?.detail).toBe('secret source')
  })

  it('keeps one completion source per editor, so its results are not dropped', () => {
    const state = EditorState.create({ doc: '{ "a": "@" }', extensions: [json(), bindingDecorations(assist)] })
    const next = state.update({ changes: { from: 9, insert: 'c' } }).state

    expect(state.languageDataAt('autocomplete', 9)).toHaveLength(1)
    expect(next.languageDataAt('autocomplete', 10)[0]).toBe(state.languageDataAt('autocomplete', 9)[0])
  })

  it('replaces the name typed so far', async () => {
    const text = '{ "endpoint": "@con|" }'
    const result = await complete(text)

    expect(result?.from).toBe(text.indexOf('|') - 3)
    expect(result?.validFor).toBeInstanceOf(RegExp)
  })

  it('does not offer the text and math forms inside them', async () => {
    expect(labels(await complete('{ "url": "@[https://@|]" }'))).toEqual(['annotation', 'config', 'primaryVault'])
  })

  it('writes the quotes of a function for JSON and for YAML', () => {
    const config = catalog.functions.find((entry) => entry.Name === 'config')!
    const annotation = catalog.functions.find((entry) => entry.Name === 'annotation')!

    expect(functionSnippet(config, '\\"')).toBe('config(\\"${}\\")')
    expect(functionSnippet(annotation, '"')).toBe('annotation("${}")')
    expect(functionSnippet({ Name: 'custom' }, '"')).toBe('custom(${})')
  })

  it('offers the sources of a sourced path', async () => {
    expect(labels(await complete('{ "password": "@(db.password, |)" }'))).toEqual(['primaryVault'])
  })

  it('offers the paths of the configuration inside @config', async () => {
    const text = '{ "endpoint": "@config(\\"/end|\\")" }'
    const result = await complete(text)

    expect(labels(result)).toEqual(['/host', '/endpoints', '/endpoints/db'])
    expect(result?.from).toBe(text.indexOf('|') - 4)
  })

  it('offers the values of the annotation, or of the ancestor named by the second argument', async () => {
    requested.length = 0

    expect(labels(await complete('{ "owner": "@annotation(\\"|\\")" }'))).toEqual(['/owner', '/tier'])
    expect(labels(await complete('{ "currency": "@annotation(\\"/|\\", \\"Subject\\")" }'))).toEqual(['/currency'])
    expect(requested).toEqual([undefined, 'Subject'])
  })

  it('offers the ancestor types in the second argument of @annotation', async () => {
    expect(labels(await complete('{ "currency": "@annotation(\\"/currency\\", \\"|\\")" }'))).toEqual([
      'Subject',
      'Context',
    ])
  })

  it('works in YAML strings', async () => {
    expect(labels(await complete("endpoint: '@co|'", 'yaml'))).toContain('config')
    expect(labels(await complete('endpoint: \'@config("/|")\'', 'yaml'))).toEqual([
      '/host',
      '/endpoints',
      '/endpoints/db',
    ])
    expect(labels(await complete('endpoint: "@(a.b, |)"', 'yaml'))).toEqual(['primaryVault'])
  })

  it('stays out of plain strings and keys', async () => {
    expect(await complete('{ "endpoint": "https://|" }')).toBeNull()
    expect(await complete('{ "@na|me": 1 }')).toBeNull()
    expect(await complete('{ "endpoint": |1 }')).toBeNull()
  })

  it('finds and decodes the string around a position', () => {
    const state = EditorState.create({ doc: '{ "a": "x", "b": "@config(\\"/a\\")", "c": 1 }' })
    const line = state.doc.line(1)
    const found = stringAt(line, line.text.indexOf('@config') + 2)!

    expect(found.delimiter).toBe('"')
    expect(found.raw).toBe('@config(\\"/a\\")')
    expect(decodeString(found.raw, found.delimiter)).toBe('@config("/a")')
    expect(decodeString('@config("it\'\'s")', "'")).toBe('@config("it\'s")')

    const yamlLine = EditorState.create({ doc: '- @a.b' }).doc.line(1)
    expect(stringAt(yamlLine, 4)).toMatchObject({ delimiter: '', raw: '@a.b' })
  })

  it('draws the card of a hover with the problem and the server note', () => {
    const card = bindingCard({
      title: 'Path',
      summary: 'A value of the `default` source.',
      problem: '`/` cannot follow `a.b`.',
      footer: true,
    })

    expect(card.querySelector('.cm-binding-card-summary code')?.textContent).toBe('default')
    expect(card.querySelector('.cm-binding-card-problem')?.textContent).toBe('/ cannot follow a.b.')
    expect(card.textContent).toContain('Evaluated by the server')
  })
})
