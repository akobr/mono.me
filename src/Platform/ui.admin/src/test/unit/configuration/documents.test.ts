import { describe, expect, it } from 'vitest'
import { buildPatch, changeCount } from '@/utils/json-patch'
import { fromDataPath, getAt, hasAt, parsePointer, removeAt, setAt, toPointer } from '@/utils/json-pointer'
import { humanizeKind, parseServerErrors, serverPathToPointer, validateAgainst } from '@/utils/schema-validation'
import { isBinding } from '@/utils/bindings'
import { cloneJson } from '@/utils/clone'
import { reactive } from 'vue'
import { diffDocuments } from '@/mock/diff'

describe('json-pointer', () => {
  it('escapes, parses and converts JSON Forms paths', () => {
    expect(toPointer(['a/b', 'c~d', 0])).toBe('/a~1b/c~0d/0')
    expect(parsePointer('/a~1b/c~0d/0')).toEqual(['a/b', 'c~d', '0'])
    expect(parsePointer('#/tiers/0')).toEqual(['tiers', '0'])
    expect(fromDataPath('tiers.0.name')).toBe('/tiers/0/name')
  })

  it('reads, sets and removes values immutably', () => {
    const value = { tiers: [{ name: 'gold' }], retries: 3 }

    expect(getAt(value, '/tiers/0/name')).toBe('gold')
    expect(hasAt(value, '/retries')).toBe(true)
    expect(hasAt(value, '/missing')).toBe(false)
    expect(setAt(value, '/schedule/cron', '* * * * *')).toEqual({ ...value, schedule: { cron: '* * * * *' } })
    expect(setAt({}, '/list/0', 'x')).toEqual({ list: ['x'] })
    expect(removeAt(value, '/tiers/0')).toEqual({ tiers: [], retries: 3 })
    expect(value.tiers).toHaveLength(1)
  })
})

describe('buildPatch', () => {
  it('guards every replace and remove with a test of the original value', () => {
    const patch = buildPatch(
      { retries: 3, mode: 'batch', notify: ['a'] },
      { retries: 5, notify: ['a', 'b'], timeout: 30 },
    )

    expect(patch).toEqual([
      { op: 'add', path: '/notify/1', value: 'b' },
      { op: 'test', path: '/mode', value: 'batch' },
      { op: 'remove', path: '/mode' },
      { op: 'test', path: '/retries', value: 3 },
      { op: 'replace', path: '/retries', value: 5 },
      { op: 'add', path: '/timeout', value: 30 },
    ])
    expect(changeCount(patch)).toBe(4)
  })

  it('is empty when nothing changed', () => {
    expect(buildPatch({ a: [1, { b: 2 }] }, { a: [1, { b: 2 }] })).toEqual([])
    expect(buildPatch(undefined, {})).toEqual([])
  })
})

describe('schema validation', () => {
  const schema = {
    type: 'object',
    properties: {
      retries: { type: 'integer', maximum: 10 },
      endpoint: { type: 'string', format: 'uri' },
      delivery: { type: 'object', properties: { to: { type: 'string', format: 'email' } }, required: ['to'] },
    },
    required: ['retries'],
  }

  it('reports pointers, including missing required properties', () => {
    expect(validateAgainst(schema, { retries: 20, endpoint: 'not a url', delivery: {} })).toEqual([
      { pointer: '/retries', message: 'must be <= 10', keyword: 'maximum' },
      { pointer: '/endpoint', message: 'must match format "uri"', keyword: 'format' },
      { pointer: '/delivery/to', message: 'to is required', keyword: 'required' },
    ])
    expect(validateAgainst(schema, {})).toEqual([
      { pointer: '/retries', message: 'retries is required', keyword: 'required' },
    ])
  })

  it('skips binding values, which the server evaluates first', () => {
    expect(validateAgainst(schema, { retries: '@rst.invoicing/retries', endpoint: '@sbt.a/baseUrl' })).toEqual([])
    expect(validateAgainst(schema, { retries: 1, delivery: { $jsonlogic: { var: 'x' } } })).toEqual([])
    expect(isBinding('@x')).toBe(true)
    expect(isBinding({ $jsone: {} })).toBe(true)
    expect(isBinding('x@y')).toBe(false)
  })

  it('selects the draft from $schema and tolerates broken schemas', () => {
    expect(
      validateAgainst({ $schema: 'https://json-schema.org/draft/2020-12/schema', prefixItems: [{ type: 'number' }] }, [
        'x',
      ]),
    ).toHaveLength(1)
    expect(validateAgainst({ type: 'nope' }, 1)).toEqual([])
  })

  it('maps 409 errors of the API to pointers', () => {
    expect(serverPathToPointer('#/endpoints[0].url')).toBe('/endpoints/0/url')
    expect(serverPathToPointer('#')).toBe('')
    expect(humanizeKind('NumberTooBig')).toBe('Number too big')
    expect(
      parseServerErrors(
        [
          { AnnotationKey: 'exe.a.b.c', Errors: ['#/retries: NumberTooBig', 'Something else'] },
          { AnnotationKey: 'exe.other', Errors: ['#/x: StringExpected'] },
        ],
        'exe.a.b.c',
      ),
    ).toEqual([
      { pointer: '/retries', message: 'Number too big', keyword: 'server' },
      { pointer: '', message: 'Something else', keyword: 'server' },
    ])
  })
})

describe('cloneJson', () => {
  it('copies reactive proxies, which structuredClone rejects', () => {
    const value = reactive({ a: [1, { b: 2 }] })

    expect(() => structuredClone(value)).toThrow()
    expect(cloneJson(value)).toEqual({ a: [1, { b: 2 }] })
  })
})

describe('mock diff (server hunk rules)', () => {
  it('counts lines and merges hunks closer than 7 unchanged lines', () => {
    const before = Object.fromEntries(Array.from({ length: 30 }, (_, index) => [`k${index}`, index]))
    const after = { ...before, k2: 'x', k8: 'y', k25: 'z' }
    const result = diffDocuments(before, after)

    expect(result.Stats).toEqual({ Additions: 3, Deletions: 3, Unchanged: 29 })
    expect(result.Hunks).toHaveLength(2)
    expect(result.Hunks![0]).toMatchObject({ OldStart: 1, NewStart: 1 })
  })
})
