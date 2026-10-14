import { describe, expect, it } from 'vitest'
import {
  ancestorKeyOf,
  ancestorTypesOf,
  ancestorTypesOfType,
  bindingCompletionContext,
  catalogView,
  explainBinding,
  pointersOf,
  quoteFor,
  quotedArguments,
} from '@/utils/binding-language'

const server = catalogView({
  Functions: catalogView().functions,
  Sources: [
    { Name: 'primaryVault', Summary: 'Secrets.', IsDefault: false, IsSecret: true, Examples: [] },
    { Name: 'default', Summary: 'Plain values.', IsDefault: true, IsSecret: false, Examples: [] },
  ],
})
const noDefault = catalogView({ Functions: catalogView().functions, Sources: [] })
const fallback = catalogView()

describe('binding language (424-10)', () => {
  describe('explainBinding', () => {
    it('classifies each form', () => {
      expect(explainBinding('@config("/maxPrice")', server)).toMatchObject({ kind: 'function', name: 'config' })
      expect(explainBinding('@annotation("/currency", "Subject")', server)).toMatchObject({ kind: 'function' })
      expect(explainBinding('@(db.password, primaryVault)', server)).toMatchObject({
        kind: 'sourced',
        name: 'primaryVault',
        title: 'Secret source',
      })
      expect(explainBinding('@database.connectionString', server)).toMatchObject({
        kind: 'path',
        summary: 'Plain values.',
      })
      expect(explainBinding('@[https://@config("/host")/api]', server)).toMatchObject({ kind: 'interpolation' })
      expect(explainBinding('@{(@config("/base") + 10) * 2}', server)).toMatchObject({ kind: 'math' })
      for (const text of ['@config("/a")', '@(a.b, primaryVault)', '@a.b', '@[x]', '@{1}']) {
        expect(explainBinding(text, server)?.problem, text).toBeUndefined()
      }
    })

    it('ignores values that are not bindings', () => {
      expect(explainBinding('plain', server)).toBeUndefined()
    })

    it('reports the common mistakes', () => {
      expect(explainBinding('@sbt.northwind/baseUrl', server)).toMatchObject({ kind: 'invalid' })
      expect(explainBinding('@sbt.northwind/baseUrl', server)?.problem).toContain('`/` cannot follow `sbt.northwind`')
      expect(explainBinding('@config(maxPrice)', server)?.problem).toBe(
        'Quote the argument: `@config("maxPrice")`. A bare path is read from the default source first.',
      )
      expect(explainBinding('@config("/a"', server)).toMatchObject({
        kind: 'invalid',
        problem: 'A function call ends with `)`.',
      })
      expect(explainBinding('@lookup("/a")', server)?.problem).toBe('No function `lookup` is registered.')
      expect(explainBinding('@(a.b, otherVault)', server)?.problem).toBe('No source `otherVault` is registered.')
      expect(explainBinding('@(a-b, primaryVault)', server)).toMatchObject({ kind: 'invalid' })
      expect(explainBinding('@[https://@host', server)).toMatchObject({ kind: 'invalid' })
      expect(explainBinding('@[ends with \\]', server)).toMatchObject({ kind: 'invalid' })
      expect(explainBinding('@{1 + 2', server)?.problem).toBe('A math expression ends with `}`.')
      expect(explainBinding('@[x @lookup("/a")]', server)?.problem).toBe('No function `lookup` is registered.')
      expect(explainBinding('@', server)?.problem).toBe('Nothing follows `@`.')
      expect(explainBinding('@-x', server)?.problem).toContain('`-` cannot follow `@`')
    })

    it('warns about a path only when the server has no default source', () => {
      expect(explainBinding('@a.b', noDefault)?.problem).toContain('No `default` source is registered')
      expect(explainBinding('@a.b', server)?.problem).toBeUndefined()
    })

    it('does not call names unknown before the catalog of the server arrives', () => {
      expect(fallback.known).toBe(false)
      expect(explainBinding('@lookup("/a")', fallback)?.problem).toBeUndefined()
      expect(explainBinding('@(a.b, vault)', fallback)?.problem).toBeUndefined()
      expect(explainBinding('@a.b', fallback)?.problem).toBeUndefined()
    })
  })

  describe('bindingCompletionContext', () => {
    it('starts after @, also inside interpolation and math', () => {
      expect(bindingCompletionContext('@')).toEqual({ kind: 'start', nested: false, word: '' })
      expect(bindingCompletionContext('@con')).toEqual({ kind: 'start', nested: false, word: 'con' })
      expect(bindingCompletionContext('@[https://@ho')).toEqual({ kind: 'start', nested: true, word: 'ho' })
      expect(bindingCompletionContext('@{(@')).toEqual({ kind: 'start', nested: true, word: '' })
    })

    it('completes the source of a sourced path', () => {
      expect(bindingCompletionContext('@(db.password, ')).toEqual({ kind: 'source', word: '' })
      expect(bindingCompletionContext('@(db.password,pri')).toEqual({ kind: 'source', word: 'pri' })
    })

    it('completes inside quoted arguments', () => {
      expect(bindingCompletionContext('@config("')).toEqual({ kind: 'argument', fn: 'config', index: 0, word: '' })
      expect(bindingCompletionContext('@config("/end')).toEqual({
        kind: 'argument',
        fn: 'config',
        index: 0,
        word: '/end',
      })
      expect(bindingCompletionContext('@annotation("/a", "Sub')).toEqual({
        kind: 'argument',
        fn: 'annotation',
        index: 1,
        word: 'Sub',
      })
      expect(bindingCompletionContext('@[x @config("/h')).toMatchObject({ kind: 'argument', fn: 'config', word: '/h' })
    })

    it('offers nothing elsewhere', () => {
      expect(bindingCompletionContext('plain')).toEqual({ kind: 'none' })
      expect(bindingCompletionContext('@a.b')).toEqual({ kind: 'none' })
      expect(bindingCompletionContext('@config("/a") ')).toEqual({ kind: 'none' })
      expect(bindingCompletionContext('@[mail \\@')).toEqual({ kind: 'none' })
    })
  })

  it('reads the quoted arguments of a call', () => {
    expect(quotedArguments('@annotation("/a", "Subject")', 'annotation')).toEqual(['/a', 'Subject'])
    expect(quotedArguments('@config("/a")', 'annotation')).toEqual([])
  })

  it('escapes the quotes of the language for JSON and YAML strings', () => {
    expect(quoteFor('"')).toBe('\\"')
    expect(quoteFor("'")).toBe('"')
    expect(quoteFor('')).toBe('"')
  })

  it('lists JSON Pointers breadth first and skips envelopes', () => {
    const document = {
      host: 'a',
      'a/b': { 'c~d': 1 },
      tiers: [{ name: 'gold' }],
      rule: { $binding: 'jlogic', $definition: { var: 'x' } },
    }
    expect(pointersOf(document)).toEqual([
      '/host',
      '/a~1b',
      '/tiers',
      '/rule',
      '/a~1b/c~0d',
      '/tiers/0',
      '/tiers/0/name',
    ])
    expect(pointersOf(document, 2)).toEqual(['/host', '/a~1b'])
    expect(pointersOf(undefined)).toEqual([])
  })

  it('finds the ancestor keys like TryGetAncestorKey', () => {
    const key = 'uxe.northwind.invoicing.prod.pdf'
    expect(ancestorKeyOf(key, 'Subject')).toBe('sbt.northwind')
    expect(ancestorKeyOf(key, 'responsibility')).toBe('rst.invoicing')
    expect(ancestorKeyOf(key, 'Usage')).toBe('usg.northwind.invoicing')
    expect(ancestorKeyOf(key, 'Context')).toBe('cnt.northwind.prod')
    expect(ancestorKeyOf(key, 'Execution')).toBe('exe.northwind.invoicing.prod')
    expect(ancestorKeyOf(key, 'Unit')).toBe('unt.invoicing.pdf')
    expect(ancestorKeyOf(key, 'UnitOfExecution')).toBeUndefined()
    expect(ancestorKeyOf('rst.invoicing', 'Subject')).toBeUndefined()
    expect(ancestorKeyOf('exe.northwind.invoicing.prod', 'Unit')).toBeUndefined()
    expect(ancestorKeyOf('exe.northwind.invoicing.prod', 'Execution')).toBe('exe.northwind.invoicing.prod')
    expect(ancestorKeyOf('exe.northwind.invoicing.prod', 'Planet')).toBeUndefined()
    expect(ancestorKeyOf('bad', 'Subject')).toBeUndefined()
  })

  it('lists the types @annotation accepts for a key or a type', () => {
    expect(ancestorTypesOf('exe.northwind.invoicing.prod')).toEqual([
      'Responsibility',
      'Subject',
      'Usage',
      'Context',
      'Execution',
    ])
    expect(ancestorTypesOf('sbt.northwind')).toEqual(['Subject'])
    expect(ancestorTypesOf(undefined)).toHaveLength(6)
    expect(ancestorTypesOfType('cnt')).toEqual(['Subject', 'Context'])
  })
})
