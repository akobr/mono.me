import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type LocationQuery, type RouteLocationRaw } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { deleteAnnotation, setAnnotation, setConfiguration } from '@42for.net/2splatform.sdk'
import { useOtherViews } from '@/composables/useOtherViews'
import { mockHandlers } from '@/mock/all-handlers'
import { random } from '@/mock/annotations'
import { useSessionStore } from '@/stores/session'
import { useViewTransferStore } from '@/stores/view-transfer'
import {
  compareFilters,
  compareLabel,
  compareLocation,
  compareSelectionPath,
  compareTexts,
  copyChanges,
  fitsInAddress,
  keySetId,
  maxKeyQueryLength,
  parseCompareQuery,
  uniqueKeys,
  viewTransferLimit,
  type CompareFilter,
} from '@/utils/view-compare'
import { annotationText, canonicalJson, readAnnotation, readConfiguration } from '@/utils/view-documents'
import { mountScreen } from '../../helpers'

// The address of the comparison of a selection across two views, the result of comparing two documents, and what the
// browser keeps for it (spec "Copy and Compare Annotations and Configurations Between Views", sections 3 and 8). The
// documents and the views come from the stateful mock API of the e2e mode, served by msw/node.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.events.removeAllListeners()
  server.resetHandlers()
})
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const storageKey = '2s-admin:view-transfer'

const router = createRouter({
  history: createMemoryHistory(),
  routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
})
/** The address as the router writes it into the address bar. */
const addressOf = (location: RouteLocationRaw) => router.resolve(location).fullPath
/** The query as the router reads it from an address. */
const queryOf = (address: string) => router.resolve(address).query

const none = () => undefined
const numbered = (count: number, prefix = 'sbt.customer-') =>
  Array.from({ length: count }, (_, index) => `${prefix}${String(index).padStart(4, '0')}`)

/** Names that a key may hold: a name is any text without a dot and without spaces around it. */
const awkwardKeys = [
  'sbt.north wind',
  'sbt.a,b',
  'sbt.100%',
  'sbt.50%25 off',
  'sbt.žluťoučký kůň',
  'sbt.日本',
  'sbt.😀',
  'sbt.a+b',
  'sbt.a&key=b',
  'sbt.a#b',
  'sbt.a?b',
  'sbt.a/b',
  'sbt.a=b',
  "sbt.o'brien",
  'sbt.a;b',
  'sbt."quoted"',
  'usg.north wind.in voicing',
  'exe.a b.c,d.e%f',
]

describe('uniqueKeys', () => {
  it('drops duplicates and sorts the keys', () => {
    expect(uniqueKeys(['usg.b.c', 'sbt.b', 'sbt.a', 'sbt.b', 'usg.b.c'])).toEqual(['sbt.a', 'sbt.b', 'usg.b.c'])
    expect(uniqueKeys([])).toEqual([])
  })

  it('leaves the list it is given as it is', () => {
    const keys = Object.freeze(['sbt.b', 'sbt.a', 'sbt.b'])

    expect(uniqueKeys(keys)).toEqual(['sbt.a', 'sbt.b'])
    expect(keys).toEqual(['sbt.b', 'sbt.a', 'sbt.b'])
  })
})

describe('fitsInAddress', () => {
  it('takes a few keys, and no keys', () => {
    expect(fitsInAddress([])).toBe(true)
    expect(fitsInAddress(['sbt.northwind'])).toBe(true)
    expect(fitsInAddress(['sbt.northwind', 'usg.northwind.invoicing', 'exe.northwind.invoicing.prod'])).toBe(true)
  })

  it('counts "&key=" and the encoded key of every key, up to 1,500 characters', () => {
    expect(maxKeyQueryLength).toBe(1500)
    // one key of 1,495 characters is exactly the limit
    expect(fitsInAddress([`sbt.${'a'.repeat(1491)}`])).toBe(true)
    expect(fitsInAddress([`sbt.${'a'.repeat(1492)}`])).toBe(false)
    // 100 keys of 10 characters are 1,500; one more character is too much
    expect(fitsInAddress(numbered(100, 'sbt.aa'))).toBe(true)
    expect(fitsInAddress([...numbered(99, 'sbt.aa'), 'sbt.aa00999'])).toBe(false)
  })

  it('takes about 40 keys of a usual length', () => {
    // 34 characters, as exe.northwind.invoicing.production
    const keys = numbered(60, 'exe.northwind.invoicing.produc')

    expect(fitsInAddress(keys.slice(0, 38))).toBe(true)
    expect(fitsInAddress(keys.slice(0, 39))).toBe(false)
  })

  it('counts a key once, however often it is selected', () => {
    const key = `sbt.${'a'.repeat(900)}`

    expect(fitsInAddress([key, key, key])).toBe(true)
    expect(fitsInAddress([key, `${key}b`])).toBe(false)
  })

  it('counts the characters of the address, not of the key', () => {
    // 250 letters of two bytes are 1,500 characters in an address, and "sbt." and "&key=" come on top
    expect(fitsInAddress([`sbt.${'a'.repeat(250)}`])).toBe(true)
    expect(fitsInAddress([`sbt.${'ž'.repeat(250)}`])).toBe(false)
    expect(fitsInAddress([`sbt.${'ž'.repeat(248)}`])).toBe(true)
    // a space and a percent sign are three characters
    expect(fitsInAddress([`sbt.${' '.repeat(497)}`])).toBe(true)
    expect(fitsInAddress([`sbt.${'%'.repeat(498)}`])).toBe(false)
  })

  it('never counts less than the router writes, so an address that fits stays below 2,000 characters', () => {
    const empty = addressOf(compareLocation(scope, { kind: 'annotations', keys: [] })).length
    for (const key of awkwardKeys) {
      const written = addressOf(compareLocation(scope, { kind: 'annotations', keys: [key] })).length - empty
      // "?" or "&", "key=" and the key
      expect(written, key).toBeLessThanOrEqual(encodeURIComponent(key).length + 5)
    }

    // the longest names of an organization, a project and two views, and as many awkward keys as fit
    const long = { org: 'o'.repeat(63), project: 'p'.repeat(63), view: 'v'.repeat(63) }
    const next = random(1500)
    for (let round = 0; round < 50; round++) {
      const keys: string[] = []
      for (;;) {
        const key = `${awkwardKeys[Math.floor(next() * awkwardKeys.length)]}-${Math.floor(next() * 1e6)}`
        if (!fitsInAddress([...keys, key])) {
          break
        }
        keys.push(key)
      }
      const address = addressOf(
        compareLocation(long, { kind: 'configurations', keys, to: 't'.repeat(63), content: 'effective' }),
      )

      expect(keys.length).toBeGreaterThan(20)
      expect(address.length).toBeLessThan(2000)
    }
  })
})

describe('keySetId', () => {
  it('is the same for the same keys in any order, with or without duplicates', () => {
    const id = keySetId(['sbt.northwind', 'usg.northwind.invoicing', 'sbt.contoso'])

    expect(keySetId(['usg.northwind.invoicing', 'sbt.contoso', 'sbt.northwind'])).toBe(id)
    expect(keySetId(['sbt.contoso', 'sbt.northwind', 'sbt.contoso', 'usg.northwind.invoicing', 'sbt.northwind'])).toBe(
      id,
    )
    // and the same on every call and in every tab: it is in an address
    expect(keySetId(['sbt.northwind', 'usg.northwind.invoicing', 'sbt.contoso'])).toBe(id)
  })

  it('is a short text of letters and digits', () => {
    for (const keys of [[], ['sbt.northwind'], numbered(100), awkwardKeys]) {
      expect(keySetId(keys)).toMatch(/^[0-9a-z]{2,14}$/)
    }
  })

  it('is another one for other keys', () => {
    const id = keySetId(['sbt.northwind', 'sbt.contoso'])

    expect(keySetId(['sbt.northwind'])).not.toBe(id)
    expect(keySetId(['sbt.northwind', 'sbt.contoso', 'sbt.fabrikam'])).not.toBe(id)
    expect(keySetId(['sbt.northwind', 'sbt.Contoso'])).not.toBe(id)
    expect(keySetId(['sbt.northwind', 'cnt.contoso'])).not.toBe(id)
    expect(keySetId([])).not.toBe(id)
    // the keys, not their joined text
    expect(keySetId(['sbt.ab', 'sbt.c'])).not.toBe(keySetId(['sbt.a', 'sbt.bc']))
    expect(keySetId(['sbt.a,sbt.b'])).not.toBe(keySetId(['sbt.a', 'sbt.b']))
    expect(keySetId(['sbt.a sbt.b'])).not.toBe(keySetId(['sbt.a', 'sbt.b']))
  })

  it('tells thousands of selections of one catalog apart', () => {
    const next = random(8)
    const catalog = numbered(500)
    const ids = new Set<string>()
    const selections = new Set<string>()

    // every single key, every list of the first keys, and random selections of up to 120 keys
    const lists = [
      ...catalog.map((key) => [key]),
      ...catalog.map((_, index) => catalog.slice(0, index + 1)),
      ...Array.from({ length: 5000 }, () => {
        const size = 1 + Math.floor(next() * 120)
        return Array.from({ length: size }, () => catalog[Math.floor(next() * catalog.length)]!)
      }),
    ]
    for (const keys of lists) {
      ids.add(keySetId(keys))
      selections.add(uniqueKeys(keys).join('\n'))
    }

    expect(selections.size).toBeGreaterThan(5000)
    expect(ids.size).toBe(selections.size)
  })
})

describe('compareLocation', () => {
  it('is the page of the selection below Compare views, with the keys as repeated parameters', () => {
    const location = compareLocation(scope, {
      kind: 'annotations',
      keys: ['usg.northwind.invoicing', 'sbt.northwind'],
      to: 'staging',
    })

    expect(location).toEqual({
      path: '/acme/billing/default/modeling/compare/selection',
      query: { kind: 'annotations', to: 'staging', key: ['sbt.northwind', 'usg.northwind.invoicing'] },
    })
    expect(compareSelectionPath(scope)).toBe('/acme/billing/default/modeling/compare/selection')
    // the address of the spec
    expect(addressOf(location)).toBe(
      '/acme/billing/default/modeling/compare/selection?kind=annotations&to=staging&key=sbt.northwind&key=usg.northwind.invoicing',
    )
  })

  it('writes the keys sorted and once, whatever the order of the selection', () => {
    const one = compareLocation(scope, { kind: 'configurations', keys: ['sbt.b', 'sbt.a', 'sbt.c'] })
    const other = compareLocation(scope, { kind: 'configurations', keys: ['sbt.c', 'sbt.b', 'sbt.a', 'sbt.b'] })

    expect(addressOf(other)).toBe(addressOf(one))
    expect(addressOf(one)).toBe(
      '/acme/billing/default/modeling/compare/selection?kind=configurations&key=sbt.a&key=sbt.b&key=sbt.c',
    )
  })

  it('names the target only when there is one', () => {
    const query = (to?: string) =>
      (compareLocation(scope, { kind: 'annotations', keys: ['sbt.a'], to }) as { query: LocationQuery }).query

    expect(query('staging').to).toBe('staging')
    expect('to' in query()).toBe(false)
    expect('to' in query('')).toBe(false)
  })

  it('names the content only for effective configurations', () => {
    const query = (kind: 'annotations' | 'configurations', content?: 'own' | 'effective') =>
      (compareLocation(scope, { kind, keys: ['sbt.a'], content }) as { query: LocationQuery }).query

    expect(query('configurations', 'effective').content).toBe('effective')
    expect('content' in query('configurations', 'own')).toBe(false)
    expect('content' in query('configurations')).toBe(false)
    // annotations have one document
    expect('content' in query('annotations', 'effective')).toBe(false)
  })

  it('carries the id of a remembered list instead of the keys', () => {
    const keys = numbered(60)
    const location = compareLocation(scope, { kind: 'configurations', keys, to: 'staging', set: 'abc123' })

    expect(location).toEqual({
      path: '/acme/billing/default/modeling/compare/selection',
      query: { kind: 'configurations', to: 'staging', set: 'abc123' },
    })
    expect(addressOf(location)).toBe(
      '/acme/billing/default/modeling/compare/selection?kind=configurations&to=staging&set=abc123',
    )
  })

  it('has no key parameter without keys', () => {
    expect(addressOf(compareLocation(scope, { kind: 'annotations', keys: [] }))).toBe(
      '/acme/billing/default/modeling/compare/selection?kind=annotations',
    )
  })

  it('writes keys with spaces, commas, percent signs and letters beyond ASCII so that they are read back', () => {
    const location = compareLocation(scope, { kind: 'annotations', keys: awkwardKeys, to: 'staging' })
    const address = addressOf(location)

    const request = parseCompareQuery(queryOf(address), 'default', none)
    expect(request.keys).toEqual(awkwardKeys.toSorted())
    expect(request).toMatchObject({ kind: 'annotations', to: 'staging', lost: false, truncated: false })
    // and as a browser, or whoever gets the link, reads the address
    expect(new URL(address, 'https://admin.example').searchParams.getAll('key')).toEqual(awkwardKeys.toSorted())
    expect(address).toMatch(/^[\x21-\x7e]+$/)
  })

  it('reads back what it wrote for every kind, target and content', () => {
    for (const kind of ['annotations', 'configurations'] as const) {
      for (const to of [undefined, 'staging']) {
        for (const content of [undefined, 'own', 'effective'] as const) {
          const keys = ['usg.northwind.invoicing', 'sbt.northwind']
          const address = addressOf(compareLocation(scope, { kind, keys, to, content }))

          expect(parseCompareQuery(queryOf(address), 'default', none), address).toEqual({
            kind,
            keys: ['sbt.northwind', 'usg.northwind.invoicing'],
            to,
            content: kind === 'configurations' && content === 'effective' ? 'effective' : 'own',
            set: undefined,
            lost: false,
            truncated: false,
          })
        }
      }
    }
  })
})

describe('parseCompareQuery', () => {
  it('reads an address without a query as no comparison yet', () => {
    expect(parseCompareQuery({}, 'default', none)).toEqual({
      kind: 'configurations',
      keys: [],
      to: undefined,
      content: 'own',
      set: undefined,
      lost: false,
      truncated: false,
    })
  })

  it('reads the kind: annotations, and configurations for anything else', () => {
    const kind = (value: LocationQuery[string] | undefined) =>
      parseCompareQuery(value === undefined ? {} : { kind: value }, 'default', none).kind

    expect(kind('annotations')).toBe('annotations')
    expect(kind('configurations')).toBe('configurations')
    expect(kind(undefined)).toBe('configurations')
    expect(kind('')).toBe('configurations')
    expect(kind(null)).toBe('configurations')
    expect(kind('Annotations')).toBe('configurations')
    expect(kind('schemas')).toBe('configurations')
    // a repeated parameter counts with its first value
    expect(kind(['annotations', 'configurations'])).toBe('annotations')
    expect(kind(['configurations', 'annotations'])).toBe('configurations')
  })

  it('reads one key and several keys', () => {
    expect(parseCompareQuery({ key: 'sbt.northwind' }, 'default', none).keys).toEqual(['sbt.northwind'])
    expect(parseCompareQuery({ key: ['sbt.northwind'] }, 'default', none).keys).toEqual(['sbt.northwind'])
    expect(parseCompareQuery({ key: ['sbt.northwind', 'sbt.contoso'] }, 'default', none).keys).toEqual([
      'sbt.contoso',
      'sbt.northwind',
    ])
    // as the router reads them from an address
    expect(parseCompareQuery(queryOf('/x?key=sbt.northwind'), 'default', none).keys).toEqual(['sbt.northwind'])
    expect(parseCompareQuery(queryOf('/x?key=sbt.b&kind=annotations&key=sbt.a'), 'default', none)).toMatchObject({
      kind: 'annotations',
      keys: ['sbt.a', 'sbt.b'],
    })
  })

  it('drops duplicates and sorts the keys', () => {
    const query = { key: ['usg.b.c', 'sbt.b', 'sbt.a', 'sbt.b', 'usg.b.c', 'sbt.a'] }

    expect(parseCompareQuery(query, 'default', none)).toMatchObject({
      keys: ['sbt.a', 'sbt.b', 'usg.b.c'],
      truncated: false,
    })
  })

  it('skips a key parameter without a value', () => {
    expect(parseCompareQuery({ key: [null, '', 'sbt.a', null] }, 'default', none).keys).toEqual(['sbt.a'])
    expect(parseCompareQuery({ key: null }, 'default', none).keys).toEqual([])
    expect(parseCompareQuery({ key: '' }, 'default', none).keys).toEqual([])
    expect(parseCompareQuery(queryOf('/x?key&key=&key=sbt.a'), 'default', none).keys).toEqual(['sbt.a'])
  })

  it('takes no target that names the view of the route', () => {
    expect(parseCompareQuery({ to: 'staging' }, 'default', none).to).toBe('staging')
    expect(parseCompareQuery({ to: 'default' }, 'default', none).to).toBeUndefined()
    expect(parseCompareQuery({ to: 'default' }, 'staging', none).to).toBe('default')
    // another spelling is another view
    expect(parseCompareQuery({ to: 'Default' }, 'default', none).to).toBe('Default')
    expect(parseCompareQuery({ to: '' }, 'default', none).to).toBeUndefined()
    expect(parseCompareQuery({ to: null }, 'default', none).to).toBeUndefined()
    expect(parseCompareQuery({}, 'default', none).to).toBeUndefined()
    expect(parseCompareQuery({ to: ['staging', 'default'] }, 'default', none).to).toBe('staging')
    expect(parseCompareQuery({ to: ['default', 'staging'] }, 'default', none).to).toBeUndefined()
    // a route without a view yet
    expect(parseCompareQuery({ to: 'staging' }, undefined, none).to).toBe('staging')
  })

  it('reads the content: effective only for configurations, own for anything else', () => {
    const content = (query: LocationQuery) => parseCompareQuery(query, 'default', none).content

    expect(content({ content: 'effective' })).toBe('effective')
    expect(content({ kind: 'configurations', content: 'effective' })).toBe('effective')
    expect(content({ kind: 'annotations', content: 'effective' })).toBe('own')
    expect(content({})).toBe('own')
    expect(content({ content: 'own' })).toBe('own')
    expect(content({ content: 'Effective' })).toBe('own')
    expect(content({ content: null })).toBe('own')
  })

  it('compares the first 100 keys of a longer address, and says so', () => {
    expect(viewTransferLimit).toBe(100)
    const keys = numbered(101)

    const exact = parseCompareQuery({ key: keys.slice(0, 100) }, 'default', none)
    expect(exact.keys).toHaveLength(100)
    expect(exact.truncated).toBe(false)

    // in key order, whatever the order of the address
    const longer = parseCompareQuery({ key: keys.toReversed() }, 'default', none)
    expect(longer.keys).toEqual(keys.slice(0, 100))
    expect(longer.truncated).toBe(true)
    expect(parseCompareQuery({ key: numbered(5000) }, 'default', none).keys).toHaveLength(100)
  })

  it('counts a key that is repeated once against the limit', () => {
    const keys = numbered(100)

    const request = parseCompareQuery({ key: [...keys, ...keys, ...keys.slice(0, 50)] }, 'default', none)

    expect(request.keys).toEqual(keys)
    expect(request.truncated).toBe(false)
  })

  it('reads the keys of a remembered list by the id of the address', () => {
    const asked: string[] = []
    const keysOf = (id: string) => {
      asked.push(id)
      return id === 'abc123' ? ['usg.b.c', 'sbt.a', 'sbt.a'] : undefined
    }

    expect(parseCompareQuery({ kind: 'annotations', to: 'staging', set: 'abc123' }, 'default', keysOf)).toEqual({
      kind: 'annotations',
      keys: ['sbt.a', 'usg.b.c'],
      to: 'staging',
      content: 'own',
      set: 'abc123',
      lost: false,
      truncated: false,
    })
    expect(asked).toEqual(['abc123'])
  })

  it('says that the list of an address is lost when the tab does not have it', () => {
    expect(parseCompareQuery({ kind: 'annotations', to: 'staging', set: 'gone' }, 'default', none)).toEqual({
      kind: 'annotations',
      keys: [],
      to: 'staging',
      content: 'own',
      set: undefined,
      lost: true,
      truncated: false,
    })
  })

  it('does not ask for a list when the address carries keys', () => {
    const keysOf = vi.fn(none)

    const request = parseCompareQuery({ key: ['sbt.a'], set: '' }, 'default', keysOf)

    expect(request).toMatchObject({ keys: ['sbt.a'], set: undefined, lost: false })
    expect(keysOf).not.toHaveBeenCalled()
  })

  it('takes the list for the keys when the address carries both', () => {
    const keysOf = (id: string) => (id === 'abc123' ? ['sbt.listed'] : undefined)

    expect(parseCompareQuery({ key: ['sbt.a'], set: 'abc123' }, 'default', keysOf).keys).toEqual(['sbt.listed'])
    // and a lost list is lost, although the address has a key
    expect(parseCompareQuery({ key: ['sbt.a'], set: 'gone' }, 'default', keysOf)).toMatchObject({
      keys: [],
      lost: true,
    })
  })

  it('limits a remembered list too, and takes an empty one as no keys', () => {
    const lists: Record<string, string[]> = { long: numbered(130).toReversed(), empty: [] }
    const keysOf = (id: string) => lists[id]

    const long = parseCompareQuery({ set: 'long' }, 'default', keysOf)
    expect(long.keys).toEqual(numbered(100))
    expect(long).toMatchObject({ set: 'long', lost: false, truncated: true })
    expect(parseCompareQuery({ set: 'empty' }, 'default', keysOf)).toMatchObject({
      keys: [],
      set: 'empty',
      lost: false,
      truncated: false,
    })
  })
})

describe('compareTexts', () => {
  const lines = (...items: string[]) => items.join('\n')
  const source = lines('{', '  "region": "eu-west",', '  "retries": 3,', '  "timeout": 30', '}')
  const target = lines('{', '  "region": "us-east",', '  "timeout": 30', '}')

  it('is absent when neither view has a document', () => {
    expect(compareTexts(undefined, undefined)).toEqual({
      status: 'absent',
      additions: 0,
      deletions: 0,
      source: '',
      target: '',
    })
  })

  it('is identical for the same text', () => {
    expect(compareTexts(source, source)).toEqual({
      status: 'identical',
      additions: 0,
      deletions: 0,
      source,
      target: source,
    })
    expect(compareTexts('{}', '{}').status).toBe('identical')
  })

  it('is different for two texts, with the lines of the source as additions and those of the target as deletions', () => {
    // the source has another region and one more member: a copy adds two lines to the target and takes one away
    expect(compareTexts(source, target)).toEqual({ status: 'different', additions: 2, deletions: 1, source, target })
    // the other way round, the same lines count the other way
    expect(compareTexts(target, source)).toEqual({
      status: 'different',
      additions: 1,
      deletions: 2,
      source: target,
      target: source,
    })
  })

  it('counts every line of a document that only the source has as added', () => {
    expect(compareTexts(source, undefined)).toEqual({
      status: 'onlySource',
      additions: 5,
      deletions: 0,
      source,
      target: '',
    })
  })

  it('counts every line of a document that only the target has as removed', () => {
    expect(compareTexts(undefined, target)).toEqual({
      status: 'onlyTarget',
      additions: 0,
      deletions: 4,
      source: '',
      target,
    })
  })

  it('tells a side without a document from a side with an empty one', () => {
    expect(compareTexts('{}', undefined)).toMatchObject({ status: 'onlySource', additions: 1, deletions: 0 })
    expect(compareTexts(undefined, '{}')).toMatchObject({ status: 'onlyTarget', additions: 0, deletions: 1 })
    expect(compareTexts('', '').status).toBe('identical')
    expect(compareTexts('', undefined).status).toBe('onlySource')
    expect(compareTexts(undefined, '').status).toBe('onlyTarget')
  })

  it('is different, never identical, for two texts that differ only in the order of their lines', () => {
    const one = lines('[', '  1,', '  2', ']')
    const other = lines('[', '  2,', '  1', ']')

    expect(compareTexts(one, other).status).toBe('different')
  })
})

describe('copyChanges', () => {
  it('is true when the documents differ or only the source has one', () => {
    expect(copyChanges(compareTexts('{\n  "a": 1\n}', '{\n  "a": 2\n}'))).toBe(true)
    expect(copyChanges(compareTexts('{}', undefined))).toBe(true)
  })

  it('is false when a copy would change nothing, or would have to delete', () => {
    expect(copyChanges(compareTexts('{}', '{}'))).toBe(false)
    expect(copyChanges(compareTexts(undefined, '{}'))).toBe(false)
    expect(copyChanges(compareTexts(undefined, undefined))).toBe(false)
    expect(copyChanges(undefined)).toBe(false)
  })
})

describe('compareLabel', () => {
  const names = { source: 'default', target: 'staging' }

  it('names the states of annotations and of effective configurations', () => {
    const label = (filter: CompareFilter) => compareLabel(filter, { ...names, own: false })

    expect(compareFilters.map(label)).toEqual([
      'All',
      'Different',
      'Only in default',
      'Only in staging',
      'Identical',
      'Not in either view',
      'Deleted in staging',
      'Effective differs',
      'Failed',
    ])
  })

  it('names the states of own configurations by the stored document', () => {
    const label = (filter: CompareFilter) => compareLabel(filter, { ...names, own: true })

    expect(compareFilters.map(label)).toEqual([
      'All',
      'Different',
      'Own configuration only in default',
      'Own configuration only in staging',
      'Identical',
      'No own configuration',
      'Deleted in staging',
      'Effective differs',
      'Failed',
    ])
  })

  it('lists every filter once, in the order of the summary', () => {
    expect(compareFilters).toEqual([
      'all',
      'different',
      'onlySource',
      'onlyTarget',
      'identical',
      'absent',
      'deleted',
      'effectiveDiffers',
      'failed',
    ])
    // every state of compareTexts is one of them
    for (const result of [
      compareTexts(undefined, undefined),
      compareTexts('a', 'a'),
      compareTexts('a', 'b'),
      compareTexts('a', undefined),
      compareTexts(undefined, 'a'),
    ]) {
      expect(compareFilters).toContain(result.status)
    }
  })
})

describe('view transfer store', () => {
  const stored = () => JSON.parse(sessionStorage.getItem(storageKey) ?? 'null') as [string, string[]][] | null
  /** Another page load of the tab: new stores over the same sessionStorage. */
  const reload = () => setActivePinia(createPinia())
  const listOf = (index: number) => [`sbt.list-${index}`, `sbt.other-${index}`]

  it('remembers the keys of a selection by their id, distinct and sorted', () => {
    const store = useViewTransferStore()

    const id = store.remember(['usg.b.c', 'sbt.b', 'sbt.a', 'sbt.b'])

    expect(id).toBe(keySetId(['sbt.a', 'sbt.b', 'usg.b.c']))
    expect(store.keysOf(id)).toEqual(['sbt.a', 'sbt.b', 'usg.b.c'])
    expect(store.keysOf('unknown')).toBeUndefined()
    expect(store.keysOf('')).toBeUndefined()
  })

  it('gives the same selection the same id, and keeps it once', () => {
    const store = useViewTransferStore()

    const id = store.remember(['sbt.a', 'sbt.b'])

    expect(store.remember(['sbt.b', 'sbt.a'])).toBe(id)
    expect(store.remember(['sbt.b', 'sbt.a', 'sbt.b'])).toBe(id)
    expect(store.remember(['sbt.a', 'sbt.c'])).not.toBe(id)
    expect(stored()).toHaveLength(2)
  })

  it('remembers a selection without keys as a list without keys, not as a lost one', () => {
    const store = useViewTransferStore()

    const id = store.remember([])

    expect(id).toBe(keySetId([]))
    expect(store.keysOf(id)).toEqual([])
    expect(parseCompareQuery({ set: id }, 'default', store.keysOf)).toMatchObject({ keys: [], set: id, lost: false })
  })

  it('answers the address of a long selection: the list it kept is the list that is compared', () => {
    const store = useViewTransferStore()
    const keys = numbered(80).toReversed()
    expect(fitsInAddress(keys)).toBe(false)

    const address = addressOf(
      compareLocation(scope, { kind: 'configurations', keys, to: 'staging', set: store.remember(keys) }),
    )

    expect(address.length).toBeLessThan(120)
    expect(parseCompareQuery(queryOf(address), 'default', store.keysOf)).toEqual({
      kind: 'configurations',
      keys: numbered(80),
      to: 'staging',
      content: 'own',
      set: keySetId(keys),
      lost: false,
      truncated: false,
    })
  })

  it('keeps eight lists: the ninth drops the oldest', () => {
    const store = useViewTransferStore()
    const ids = Array.from({ length: 8 }, (_, index) => store.remember(listOf(index)))
    expect(ids.every((id) => store.keysOf(id))).toBe(true)

    const ninth = store.remember(listOf(8))

    expect(store.keysOf(ids[0]!)).toBeUndefined()
    expect(ids.slice(1).map((id) => store.keysOf(id))).toEqual(
      Array.from({ length: 7 }, (_, index) => listOf(index + 1)),
    )
    expect(store.keysOf(ninth)).toEqual(listOf(8))
    expect(stored()).toHaveLength(8)
  })

  it('keeps a list that is remembered again: it is the newest then', () => {
    const store = useViewTransferStore()
    const ids = Array.from({ length: 8 }, (_, index) => store.remember(listOf(index)))

    // the first list is opened again, then two more lists come
    expect(store.remember(listOf(0).toReversed())).toBe(ids[0])
    store.remember(listOf(8))
    store.remember(listOf(9))

    expect(store.keysOf(ids[0]!)).toEqual(listOf(0))
    expect(store.keysOf(ids[1]!)).toBeUndefined()
    expect(store.keysOf(ids[2]!)).toBeUndefined()
    expect(store.keysOf(ids[3]!)).toEqual(listOf(3))
    expect(stored()!.map(([id]) => id)).toEqual([...ids.slice(3), ids[0], keySetId(listOf(8)), keySetId(listOf(9))])
  })

  it('does not make a list newer by reading it', () => {
    const store = useViewTransferStore()
    const ids = Array.from({ length: 8 }, (_, index) => store.remember(listOf(index)))

    expect(store.keysOf(ids[0]!)).toEqual(listOf(0))
    store.remember(listOf(8))

    expect(store.keysOf(ids[0]!)).toBeUndefined()
  })

  it('keeps the lists in sessionStorage, so a reload of the tab finds them', () => {
    const id = useViewTransferStore().remember(['sbt.b', 'sbt.a'])
    expect(stored()).toEqual([[id, ['sbt.a', 'sbt.b']]])
    expect(localStorage.length).toBe(0)

    reload()

    expect(useViewTransferStore().keysOf(id)).toEqual(['sbt.a', 'sbt.b'])
  })

  it('keeps the age of the lists over a reload', () => {
    const ids = Array.from({ length: 8 }, (_, index) => useViewTransferStore().remember(listOf(index)))

    reload()
    const store = useViewTransferStore()
    store.remember(listOf(8))

    expect(store.keysOf(ids[0]!)).toBeUndefined()
    expect(store.keysOf(ids[1]!)).toEqual(listOf(1))
    expect(store.keysOf(ids[7]!)).toEqual(listOf(7))
  })

  it('does not find a list in another tab, which has its own sessionStorage', () => {
    const id = useViewTransferStore().remember(['sbt.a'])

    sessionStorage.clear()
    reload()

    expect(useViewTransferStore().keysOf(id)).toBeUndefined()
    expect(parseCompareQuery({ set: id }, 'default', useViewTransferStore().keysOf)).toMatchObject({
      keys: [],
      lost: true,
    })
  })

  it('forgets the lists and the targets with clear()', () => {
    const store = useViewTransferStore()
    const id = store.remember(['sbt.a'])
    store.rememberTarget('acme.billing', 'staging')

    store.clear()

    expect(store.keysOf(id)).toBeUndefined()
    expect(store.lastTarget('acme.billing')).toBeUndefined()
    expect(sessionStorage.getItem(storageKey)).toBeNull()
    // and a list after it is kept as the first one
    const next = store.remember(['sbt.b'])
    expect(stored()).toEqual([[next, ['sbt.b']]])
  })

  it('forgets the stored lists with clear() before it has read them', () => {
    const id = useViewTransferStore().remember(['sbt.a'])
    reload()

    useViewTransferStore().clear()

    expect(sessionStorage.getItem(storageKey)).toBeNull()
    expect(useViewTransferStore().keysOf(id)).toBeUndefined()
  })

  it('is cleared on sign-out, so the next user of the tab finds no keys', async () => {
    const store = useViewTransferStore()
    const id = store.remember(['sbt.a', 'sbt.b'])
    store.rememberTarget('acme.billing', 'staging')

    await useSessionStore().signOut()

    expect(store.keysOf(id)).toBeUndefined()
    expect(store.lastTarget('acme.billing')).toBeUndefined()
    expect(sessionStorage.getItem(storageKey)).toBeNull()
    reload()
    expect(useViewTransferStore().keysOf(id)).toBeUndefined()
  })

  it('remembers the last target of each project, in memory only', () => {
    const store = useViewTransferStore()
    expect(store.lastTarget('acme.billing')).toBeUndefined()

    store.rememberTarget('acme.billing', 'staging')
    store.rememberTarget('acme.shipping', 'legacy')
    store.rememberTarget('acme.billing', 'default')

    expect(store.lastTarget('acme.billing')).toBe('default')
    expect(store.lastTarget('acme.shipping')).toBe('legacy')
    expect(store.lastTarget('northwind.portal')).toBeUndefined()
    expect(sessionStorage.length).toBe(0)
    expect(localStorage.length).toBe(0)

    reload()
    expect(useViewTransferStore().lastTarget('acme.billing')).toBeUndefined()
  })

  /**
   * Runs the body over another sessionStorage. The storage of the test environment is a proxy that a spy does not
   * come off again, so the tests replace the global instead.
   */
  function withSessionStorage(storage: () => Partial<Storage>, body: () => void) {
    const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'sessionStorage')!
    Object.defineProperty(globalThis, 'sessionStorage', { configurable: true, get: storage })
    try {
      body()
    } finally {
      Object.defineProperty(globalThis, 'sessionStorage', descriptor)
    }
  }

  const denied = (): never => {
    throw new DOMException('The operation is insecure.', 'SecurityError')
  }

  it('works in a browser that blocks storage: the lists last until the page is reloaded', () => {
    // reading window.sessionStorage itself throws there
    const reads = vi.fn(denied)

    withSessionStorage(reads, () => {
      const store = useViewTransferStore()

      const id = store.remember(['sbt.b', 'sbt.a'])

      expect(reads).toHaveBeenCalled()
      expect(store.keysOf(id)).toEqual(['sbt.a', 'sbt.b'])
      expect(store.keysOf('unknown')).toBeUndefined()
      expect(() => store.clear()).not.toThrow()
      expect(store.keysOf(id)).toBeUndefined()

      const again = store.remember(['sbt.c'])
      reload()
      expect(useViewTransferStore().keysOf(again)).toBeUndefined()
    })
  })

  it('works over a sessionStorage whose every call throws', () => {
    const storage = { getItem: vi.fn(denied), setItem: vi.fn(denied), removeItem: vi.fn(denied) }

    withSessionStorage(
      () => storage,
      () => {
        const store = useViewTransferStore()

        const id = store.remember(['sbt.b', 'sbt.a'])
        expect(store.keysOf(id)).toEqual(['sbt.a', 'sbt.b'])
        expect(() => store.clear()).not.toThrow()
        expect(store.keysOf(id)).toBeUndefined()
      },
    )

    // the read, the write and the removal were all tried
    expect(storage.getItem).toHaveBeenCalledOnce()
    expect(storage.setItem).toHaveBeenCalledOnce()
    expect(storage.removeItem).toHaveBeenCalledOnce()
  })

  it('keeps a list in memory when sessionStorage is full', () => {
    const real = sessionStorage
    const full = {
      getItem: (name: string) => real.getItem(name),
      removeItem: (name: string) => real.removeItem(name),
      setItem: vi.fn(() => {
        throw new DOMException('The quota has been exceeded.', 'QuotaExceededError')
      }),
    }
    const store = useViewTransferStore()
    let id = ''

    withSessionStorage(
      () => full,
      () => {
        id = store.remember(['sbt.a'])

        expect(full.setItem).toHaveBeenCalledOnce()
        expect(store.keysOf(id)).toEqual(['sbt.a'])
      },
    )
    expect(sessionStorage.getItem(storageKey)).toBeNull()

    // once there is room again, the next list takes the kept one with it
    const next = store.remember(['sbt.b'])
    expect(stored()).toEqual([
      [id, ['sbt.a']],
      [next, ['sbt.b']],
    ])
  })

  it('starts empty over a stored text that is no list of lists', () => {
    for (const text of ['', 'not json', 'null', '42', '"text"', '{"a":["sbt.a"]}', '[', '[1,"a",null,{},[]]']) {
      sessionStorage.setItem(storageKey, text)
      reload()
      const store = useViewTransferStore()

      expect(store.keysOf('a'), text).toBeUndefined()
      const id = store.remember(['sbt.a'])
      expect(stored(), text).toEqual([[id, ['sbt.a']]])
    }
  })

  it('skips a stored entry that is no id with its keys, and keeps the others', () => {
    const good = keySetId(['sbt.a'])
    sessionStorage.setItem(
      storageKey,
      JSON.stringify([
        ['only-an-id'],
        [1, ['sbt.x']],
        ['keys-are-a-text', 'sbt.x'],
        ['keys-are-no-texts', ['sbt.x', 7, null]],
        [good, ['sbt.a']],
        null,
      ]),
    )

    const store = useViewTransferStore()

    expect(store.keysOf('only-an-id')).toBeUndefined()
    expect(store.keysOf('keys-are-a-text')).toBeUndefined()
    expect(store.keysOf('keys-are-no-texts')).toBeUndefined()
    expect(store.keysOf(good)).toEqual(['sbt.a'])
  })
})

describe('the documents of two views', () => {
  const target = { ...scope, view: 'staging' }

  /** The results of one key as the comparison page computes them: the source is the view of the route. */
  async function compare(key: string) {
    const [here, there] = await Promise.all([
      readAnnotation(scope, key),
      readAnnotation(target, key, { includeDeleted: true }),
    ])
    const [mine, theirs] = await Promise.all([readConfiguration(scope, key), readConfiguration(target, key)])
    return {
      annotation: compareTexts(
        here.annotation && annotationText(here.annotation),
        there.annotation && annotationText(there.annotation),
      ),
      own: compareTexts(mine.own && canonicalJson(mine.own), theirs.own && canonicalJson(theirs.own)),
      effective: compareTexts(
        mine.readable ? canonicalJson(mine.effective) : undefined,
        theirs.readable ? canonicalJson(theirs.effective) : undefined,
      ),
      deleted: there.deleted,
    }
  }

  // The keys of the end-to-end tests (spec section 9), in acme/billing from default to staging.
  it('finds the annotations of the mock data different, and only in the source', async () => {
    const northwind = await compare('sbt.northwind')
    expect(northwind.annotation.status).toBe('different')
    expect(northwind.annotation.additions).toBeGreaterThan(0)
    expect(northwind.annotation.deletions).toBeGreaterThan(0)

    const initech = await compare('sbt.initech')
    expect(initech.annotation).toMatchObject({ status: 'onlySource', deletions: 0, target: '' })
    expect(initech.annotation.additions).toBe(initech.annotation.source.split('\n').length)
    expect(initech.deleted).toBe(false)
  })

  it('finds the configurations of the mock data different, identical, and only in the source', async () => {
    expect((await compare('sbt.contoso')).own.status).toBe('different')
    expect((await compare('usg.northwind.invoicing')).own).toMatchObject({
      status: 'identical',
      additions: 0,
      deletions: 0,
    })
    // its annotation is not in staging, so staging can read neither document
    const execution = await compare('exe.northwind.reporting.test')
    expect(execution.own.status).toBe('onlySource')
    expect(execution.effective.status).toBe('onlySource')
    expect(execution.annotation.status).toBe('onlySource')
  })

  it('finds a key without an own configuration in either view, whose effective documents are the same', async () => {
    const execution = await compare('exe.northwind.invoicing.prod')

    expect(execution.own).toEqual({ status: 'absent', additions: 0, deletions: 0, source: '', target: '' })
    expect(execution.effective.status).toBe('identical')
  })

  it('finds the own configurations the same while the effective ones differ by what an ancestor configures', async () => {
    // only the subject of the source gets a region, which its context inherits
    await setConfiguration(scope.org, scope.project, scope.view, 'sbt.northwind', { region: 'ap-south' })

    const context = await compare('cnt.northwind.prod')

    expect(context.own.status).toBe('absent')
    // "{}" in the target, and an object with one member in the source
    expect(context.effective).toMatchObject({ status: 'different', additions: 3, deletions: 1, target: '{}' })
    expect(copyChanges(context.own)).toBe(false)
  })

  it('tells a key that is deleted in the target from a key that was never there', async () => {
    await deleteAnnotation(scope.org, scope.project, 'staging', 'sbt.contoso')

    const contoso = await compare('sbt.contoso')
    expect(contoso.deleted).toBe(true)
    expect(contoso.annotation.status).toBe('onlySource')
    expect(contoso.own.status).toBe('onlySource')
    expect((await compare('sbt.initech')).deleted).toBe(false)
  })

  it('finds an annotation identical once the target holds the content of the source', async () => {
    const { annotation } = await readAnnotation(scope, 'sbt.northwind')

    // the route decides the view, and the members that the server owns are ignored
    await setAnnotation(scope.org, scope.project, 'staging', 'sbt.northwind', annotation!)

    const after = await compare('sbt.northwind')
    expect(after.annotation).toMatchObject({ status: 'identical', additions: 0, deletions: 0 })
    expect((await readAnnotation(target, 'sbt.northwind')).annotation?.ViewName).toBe('staging')
  })
})

describe('useOtherViews', () => {
  const mounted: { host: { unmount: () => void } }[] = []
  afterEach(() => {
    mounted.splice(0).forEach((view) => view.host.unmount())
  })

  async function mountOthers(project: string, view: string, accessMap?: Record<string, string>) {
    let others!: ReturnType<typeof useOtherViews>
    const Harness = defineComponent({
      setup() {
        others = useOtherViews()
        return () => null
      },
    })
    const screen = await mountScreen(Harness, {}, { scope: { org: 'acme', project, view }, accessMap })
    mounted.push(screen)
    return { others, queryClient: screen.queryClient }
  }

  it('lists the other views of the project, without the view of the route', async () => {
    // acme/billing has default and staging
    const { others } = await mountOthers('billing', 'default')
    expect(others.known.value).toBe(false)
    expect(others.alone.value).toBe(false)
    expect(others.names.value).toEqual([])

    await vi.waitFor(() => expect(others.known.value).toBe(true))

    expect(others.names.value).toEqual(['staging'])
    expect(others.alone.value).toBe(false)
    expect(others.has('staging')).toBe(true)
    expect(others.has('default')).toBe(false)
    expect(others.has('legacy')).toBe(false)
    expect(others.has(undefined)).toBe(false)
    expect(others.has('')).toBe(false)
  })

  it('lists the default view from another view', async () => {
    const { others } = await mountOthers('billing', 'staging')
    await vi.waitFor(() => expect(others.known.value).toBe(true))

    expect(others.names.value).toEqual(['default'])
    expect(others.has('default')).toBe(true)
    expect(others.has('staging')).toBe(false)
  })

  it('says that a project with one view is alone, once its views are known', async () => {
    // acme/shipping has only its default view
    const { others } = await mountOthers('shipping', 'default', { 'acme.shipping': 'Contributor' })
    expect(others.alone.value).toBe(false)

    await vi.waitFor(() => expect(others.known.value).toBe(true))

    expect(others.names.value).toEqual([])
    expect(others.alone.value).toBe(true)
    expect(others.has('default')).toBe(false)
  })

  it('shares the query of the Modeling header and Compare views, and asks for no discovered views', async () => {
    const requests: string[] = []
    server.events.on('request:start', ({ request }) => {
      const url = new URL(request.url)
      requests.push(`${url.pathname}${url.search}`)
    })

    const { others, queryClient } = await mountOthers('billing', 'default')
    await vi.waitFor(() => expect(others.known.value).toBe(true))

    expect(queryClient.getQueryState(['v1', 'acme', 'billing', 'views'])?.status).toBe('success')
    expect(requests.filter((path) => path.includes('/views'))).toEqual(['/api/v1/acme/billing/views'])
  })

  it('does not say alone when the views cannot be read', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/views', () => HttpResponse.json({ Message: 'No views' }, { status: 500 })),
    )
    const { others, queryClient } = await mountOthers('shipping', 'default', { 'acme.shipping': 'Contributor' })
    await vi.waitFor(() => expect(queryClient.getQueryState(['v1', 'acme', 'shipping', 'views'])?.status).toBe('error'))

    expect(others.known.value).toBe(false)
    expect(others.alone.value).toBe(false)
    expect(others.names.value).toEqual([])
  })
})
