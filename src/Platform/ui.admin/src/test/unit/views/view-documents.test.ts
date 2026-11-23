import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  deleteAnnotation,
  deleteConfiguration,
  getAnnotation,
  getConfiguration,
  isAborted,
  setAnnotation,
  setConfiguration,
  type Annotation,
  type Unit,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { random } from '@/mock/annotations'
import {
  annotationDocument,
  annotationText,
  canonicalJson,
  countOf,
  hasInexactNumber,
  ownDocument,
  readAnnotation,
  readConfiguration,
} from '@/utils/view-documents'

// The documents that are compared and copied between two views (spec "Copy and Compare Annotations and Configurations
// Between Views", section 1); the reads run against the stateful mock API of the e2e mode, served by msw/node.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.events.removeAllListeners()
  server.resetHandlers()
})
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const staging = { ...scope, view: 'staging' }
const { org, project, view } = scope

const subject = (name: string, extra: Partial<Annotation> = {}): Annotation => ({
  ProjectName: project,
  ViewName: view,
  AnnotationKey: `sbt.${name}`,
  Name: name,
  AnnotationType: 'Subject',
  ...extra,
})

/** A unit with every editable member, and the members that the server owns. */
const unit = (extra: Partial<Unit> = {}): Annotation =>
  ({
    ProjectName: project,
    ViewName: view,
    AnnotationKey: 'unt.invoicing.pdf',
    Name: 'pdf',
    AnnotationType: 'Unit',
    ResponsibilityKey: 'rst.invoicing',
    ResponsibilityName: 'invoicing',
    UnitDefinition: '*/15 * * * *',
    UnitType: 'time',
    TimeZone: 'Europe/Prague',
    ExpiresAt: '2027-01-01T00:00:00Z',
    ValidFrom: '2026-01-01T00:00:00Z',
    IsDisabled: true,
    Values: { tier: 'gold', retries: 3 },
    Labels: ['pci', 'eu'],
    DocumentationLink: 'https://docs.example.com/unt/invoicing/pdf',
    Description: 'Renders **invoices**.',
    Title: 'Invoice PDF',
    Created: { At: '2026-01-01T00:00:00Z', By: { Kind: 'System' } },
    Modified: { At: '2026-02-01T00:00:00Z', By: { Kind: 'Account', Id: 'user_1', Name: 'Ada' } },
    ...extra,
  }) as Annotation

/** The object that JSON.parse makes of the text: a member named __proto__ is its own member there. */
const parsed = (json: string) => JSON.parse(json) as Record<string, unknown>

describe('annotationDocument', () => {
  it('holds the editable members in the order of an annotation file of the view export', () => {
    const document = annotationDocument(unit())

    expect(Object.keys(document)).toEqual([
      'Title',
      'Description',
      'DocumentationLink',
      'Labels',
      'Values',
      'IsDisabled',
      'ValidFrom',
      'ExpiresAt',
      'TimeZone',
      'UnitType',
      'UnitDefinition',
    ])
    expect(document).toEqual({
      Title: 'Invoice PDF',
      Description: 'Renders **invoices**.',
      DocumentationLink: 'https://docs.example.com/unt/invoicing/pdf',
      Labels: ['eu', 'pci'],
      Values: { tier: 'gold', retries: 3 },
      IsDisabled: true,
      ValidFrom: '2026-01-01T00:00:00.000Z',
      ExpiresAt: '2027-01-01T00:00:00.000Z',
      TimeZone: 'Europe/Prague',
      UnitType: 'time',
      UnitDefinition: '*/15 * * * *',
    })
  })

  it('leaves out the key, the names, the view, the stamps, the deletion and the relations', () => {
    const execution = {
      ProjectName: project,
      ViewName: view,
      AnnotationKey: 'exe.northwind.invoicing.prod',
      Name: 'prod',
      AnnotationType: 'Execution',
      SubjectKey: 'sbt.northwind',
      SubjectName: 'northwind',
      ResponsibilityKey: 'rst.invoicing',
      ResponsibilityName: 'invoicing',
      ContextKey: 'cnt.northwind.prod',
      ContextName: 'prod',
      UsageKey: 'usg.northwind.invoicing',
      UnitNames: ['pdf'],
      Title: 'Invoicing in production',
      Created: { At: '2026-01-01T00:00:00Z', By: { Kind: 'System' } },
      Modified: { At: '2026-02-01T00:00:00Z', By: { Kind: 'System' } },
      Deletion: { DeletionId: 'del_1', RootKey: 'sbt.northwind' },
    } as Annotation

    expect(annotationDocument(execution)).toEqual({ Title: 'Invoicing in production' })
    expect(annotationDocument(subject('northwind'))).toEqual({})
  })

  it('leaves out a top-level member that is not set: null, an empty text, an empty array, an empty object', () => {
    const unset = subject('northwind', {
      Title: '',
      Description: null as unknown as string,
      DocumentationLink: undefined,
      Labels: [],
      Values: {},
      IsDisabled: null,
      ValidFrom: null,
      ExpiresAt: '' as string | null,
      TimeZone: '',
    })

    expect(annotationDocument(unset)).toEqual({})
    expect(Object.keys(annotationDocument(unset))).toEqual([])
    // a text of spaces is a text, as for the API (string.IsNullOrEmpty)
    expect(annotationDocument(subject('northwind', { Title: ' ' }))).toEqual({ Title: ' ' })
  })

  it('leaves out a disabled flag that is false or null, and keeps one that is true', () => {
    expect(annotationDocument(subject('northwind', { IsDisabled: false }))).toEqual({})
    expect(annotationDocument(subject('northwind', { IsDisabled: null }))).toEqual({})
    expect(annotationDocument(subject('northwind', { IsDisabled: undefined }))).toEqual({})
    expect(annotationDocument(subject('northwind', { IsDisabled: true }))).toEqual({ IsDisabled: true })
  })

  it('leaves nothing out inside Values: a name with null or an empty value is a value', () => {
    const values = { empty: '', nothing: null, list: [], object: {}, off: false, zero: 0, nested: { empty: '' } }

    expect(annotationDocument(subject('northwind', { Values: values }))).toEqual({ Values: values })
    // and the text shows every one of them
    expect(annotationText(subject('northwind', { Values: values }))).toBe(
      [
        '{',
        '  "Values": {',
        '    "empty": "",',
        '    "list": [],',
        '    "nested": {',
        '      "empty": ""',
        '    },',
        '    "nothing": null,',
        '    "object": {},',
        '    "off": false,',
        '    "zero": 0',
        '  }',
        '}',
      ].join('\n'),
    )
  })

  it('takes the labels as a set: distinct, in the order of their names', () => {
    const labels = ['pci', 'eu', 'pci', 'Beta', 'eu', 'ädvanced', '10', '9']

    // the ordinal order of the API: digits, capitals, small letters, then the letters beyond ASCII
    expect(annotationDocument(subject('northwind', { Labels: labels })).Labels).toEqual([
      '10',
      '9',
      'Beta',
      'eu',
      'pci',
      'ädvanced',
    ])
    // the labels of the annotation itself stay as they are
    expect(labels).toEqual(['pci', 'eu', 'pci', 'Beta', 'eu', 'ädvanced', '10', '9'])
  })

  it('takes the dates as instants: another offset of the same instant is the same date', () => {
    const utc = annotationDocument(
      subject('northwind', { ValidFrom: '2026-03-01T10:00:00Z', ExpiresAt: '2026-12-31T23:30:00Z' }),
    )
    const offset = annotationDocument(
      subject('northwind', { ValidFrom: '2026-03-01T12:00:00+02:00', ExpiresAt: '2026-12-31T18:30:00-05:00' }),
    )

    expect(utc).toEqual({ ValidFrom: '2026-03-01T10:00:00.000Z', ExpiresAt: '2026-12-31T23:30:00.000Z' })
    expect(offset).toEqual(utc)
    // the seven digits of a .NET DateTimeOffset, as the API writes a stored date
    expect(annotationDocument(subject('northwind', { ValidFrom: '2026-03-01T10:00:00.1230000+00:00' }))).toEqual({
      ValidFrom: '2026-03-01T10:00:00.123Z',
    })
    // another instant stays another date
    expect(annotationDocument(subject('northwind', { ValidFrom: '2026-03-01T10:00:00+02:00' }))).not.toEqual({
      ValidFrom: '2026-03-01T10:00:00.000Z',
    })
    // a browser keeps milliseconds, and the digits below them are kept too: the API tells such dates apart
    expect(annotationDocument(subject('northwind', { ValidFrom: '2026-03-01T10:00:00.1239999Z' }))).toEqual({
      ValidFrom: '2026-03-01T10:00:00.1239999Z',
    })
    expect(annotationDocument(subject('northwind', { ValidFrom: '2026-03-01T12:00:00.1234500+02:00' }))).toEqual({
      ValidFrom: '2026-03-01T10:00:00.12345Z',
    })
    expect(annotationText(subject('northwind', { ValidFrom: '2026-03-01T10:00:00.1234Z' }))).not.toBe(
      annotationText(subject('northwind', { ValidFrom: '2026-03-01T10:00:00.1235Z' })),
    )
  })

  it('keeps a date that is no date as it is', () => {
    expect(annotationDocument(subject('northwind', { ValidFrom: 'soon', ExpiresAt: 'never' }))).toEqual({
      ValidFrom: 'soon',
      ExpiresAt: 'never',
    })
  })

  it('adds UnitType and UnitDefinition for a unit only', () => {
    // a unit by its type, and one by its key alone
    expect(annotationDocument(unit({ Title: '', Description: '', IsDisabled: false, Labels: [], Values: {} }))).toEqual(
      {
        DocumentationLink: 'https://docs.example.com/unt/invoicing/pdf',
        ValidFrom: '2026-01-01T00:00:00.000Z',
        ExpiresAt: '2027-01-01T00:00:00.000Z',
        TimeZone: 'Europe/Prague',
        UnitType: 'time',
        UnitDefinition: '*/15 * * * *',
      },
    )
    const byKey = { AnnotationKey: 'unt.invoicing.pdf', UnitType: 'message', UnitDefinition: 'orders.created' }
    expect(annotationDocument(byKey as unknown as Annotation)).toEqual({
      UnitType: 'message',
      UnitDefinition: 'orders.created',
    })

    // a unit without a definition, and members of that name on another type
    expect(annotationDocument(unit({ UnitDefinition: '', Title: 'Invoice PDF' })).UnitDefinition).toBeUndefined()
    expect('UnitDefinition' in annotationDocument(unit({ UnitDefinition: '' }))).toBe(false)
    const stray = { ...subject('northwind'), UnitType: 'time', UnitDefinition: '* * * * *' }
    expect(annotationDocument(stray)).toEqual({})
    const unitOfExecution = { AnnotationKey: 'uxe.northwind.invoicing.prod.pdf', AnnotationType: 'UnitOfExecution' }
    expect(annotationDocument({ ...unitOfExecution, UnitType: 'time' } as unknown as Annotation)).toEqual({})
  })

  it('does not change the annotation it reads', () => {
    const annotation = unit()
    const before = JSON.stringify(annotation)
    Object.freeze(annotation)
    Object.freeze(annotation.Labels)

    annotationDocument(annotation)
    annotationText(annotation)

    expect(JSON.stringify(annotation)).toBe(before)
  })
})

describe('annotationText', () => {
  it('writes the members in their fixed order, and everything below them in the order of the names', () => {
    const annotation = unit({
      Values: { tier: 'gold', retries: 3, limits: { soft: 10, hard: [{ unit: 'mb', amount: 5 }] } },
    })

    expect(annotationText(annotation)).toBe(
      [
        '{',
        '  "Title": "Invoice PDF",',
        '  "Description": "Renders **invoices**.",',
        '  "DocumentationLink": "https://docs.example.com/unt/invoicing/pdf",',
        '  "Labels": [',
        '    "eu",',
        '    "pci"',
        '  ],',
        '  "Values": {',
        '    "limits": {',
        '      "hard": [',
        '        {',
        '          "amount": 5,',
        '          "unit": "mb"',
        '        }',
        '      ],',
        '      "soft": 10',
        '    },',
        '    "retries": 3,',
        '    "tier": "gold"',
        '  },',
        '  "IsDisabled": true,',
        '  "ValidFrom": "2026-01-01T00:00:00.000Z",',
        '  "ExpiresAt": "2027-01-01T00:00:00.000Z",',
        '  "TimeZone": "Europe/Prague",',
        '  "UnitType": "time",',
        '  "UnitDefinition": "*/15 * * * *"',
        '}',
      ].join('\n'),
    )
  })

  it('is the JSON of the document', () => {
    for (const annotation of [
      unit(),
      subject('northwind'),
      subject('northwind', { Values: { a: [1, { b: null }] } }),
    ]) {
      expect(JSON.parse(annotationText(annotation))).toEqual(annotationDocument(annotation))
    }
  })

  it('is an empty object for an annotation without content', () => {
    expect(annotationText(subject('northwind'))).toBe('{}')
    expect(annotationText(subject('northwind', { Title: '', Labels: [], Values: {}, IsDisabled: false }))).toBe('{}')
  })

  it('is the same text for two annotations that the API takes as unchanged', () => {
    const stored = subject('northwind', {
      Title: 'Northwind',
      Description: undefined,
      Labels: ['eu', 'pci'],
      Values: { tier: 'gold', limits: { soft: 10, hard: 20 } },
      IsDisabled: false,
      ValidFrom: '2026-03-01T10:00:00+00:00',
      TimeZone: 'UTC',
      Modified: { At: '2026-02-01T00:00:00Z', By: { Kind: 'System' } },
    })
    const other = {
      ...subject('northwind', {
        // another order of the members, of the labels and of the values, another offset, and empty for missing
        TimeZone: 'UTC',
        ValidFrom: '2026-03-01T11:00:00+01:00',
        ExpiresAt: null,
        Values: { limits: { hard: 20, soft: 10 }, tier: 'gold' },
        Labels: ['pci', 'eu', 'pci'],
        DocumentationLink: '',
        Description: '',
        Title: 'Northwind',
      }),
      ViewName: 'staging',
      Modified: { At: '2026-11-01T00:00:00Z', By: { Kind: 'Account' as const, Id: 'user_1' } },
    }

    expect(annotationText(other)).toBe(annotationText(stored))
  })

  it('is another text for every difference that the API takes as a change', () => {
    const base = subject('northwind', { Title: 'Northwind', Labels: ['eu'], Values: { tier: 'gold' } })
    const text = annotationText(base)

    for (const change of [
      { Title: 'northwind' },
      { Description: ' ' },
      { DocumentationLink: 'https://docs.example.com' },
      { Labels: ['eu', 'pci'] },
      { Labels: ['EU'] },
      { Labels: [] },
      { Values: { tier: 'silver' } },
      { Values: { tier: 'gold', retries: null } },
      { Values: { tier: 'gold', retries: '' } },
      { Values: { Tier: 'gold' } },
      { Values: undefined },
      { IsDisabled: true },
      { ValidFrom: '2026-03-01T10:00:00Z' },
      { ExpiresAt: '2026-03-01T10:00:00Z' },
      { TimeZone: 'UTC' },
    ] satisfies Partial<Annotation>[]) {
      expect(annotationText({ ...base, ...change }), JSON.stringify(change)).not.toBe(text)
    }
  })

  it('keeps a value named __proto__, and names that look like numbers in the order of names', () => {
    const values = parsed('{"b": 1, "__proto__": {"admin": true}, "10": "ten", "9": "nine"}')

    expect(annotationText(subject('northwind', { Values: values }))).toBe(
      [
        '{',
        '  "Values": {',
        '    "10": "ten",',
        '    "9": "nine",',
        '    "__proto__": {',
        '      "admin": true',
        '    },',
        '    "b": 1',
        '  }',
        '}',
      ].join('\n'),
    )
  })
})

describe('canonicalJson', () => {
  it('writes JSON with two spaces, the members of every object in the order of their names', () => {
    const document = {
      timeout: 30,
      endpoint: '@config("/baseUrl")',
      tiers: [
        { name: 'gold', limit: 100 },
        { limit: 50, name: 'silver' },
      ],
      headers: { 'X-Tenant': 'northwind', Accept: 'application/json' },
      enabled: true,
    }

    expect(canonicalJson(document)).toBe(
      [
        '{',
        '  "enabled": true,',
        '  "endpoint": "@config(\\"/baseUrl\\")",',
        '  "headers": {',
        '    "Accept": "application/json",',
        '    "X-Tenant": "northwind"',
        '  },',
        '  "tiers": [',
        '    {',
        '      "limit": 100,',
        '      "name": "gold"',
        '    },',
        '    {',
        '      "limit": 50,',
        '      "name": "silver"',
        '    }',
        '  ],',
        '  "timeout": 30',
        '}',
      ].join('\n'),
    )
  })

  it('keeps the order of arrays, also of arrays in arrays', () => {
    expect(canonicalJson({ list: [3, 1, 2] })).not.toBe(canonicalJson({ list: [1, 2, 3] }))
    expect(canonicalJson([[2, 1], [], [{ b: 1, a: [{ d: 1, c: 2 }] }]])).toBe(
      [
        '[',
        '  [',
        '    2,',
        '    1',
        '  ],',
        '  [],',
        '  [',
        '    {',
        '      "a": [',
        '        {',
        '          "c": 2,',
        '          "d": 1',
        '        }',
        '      ],',
        '      "b": 1',
        '    }',
        '  ]',
        ']',
      ].join('\n'),
    )
  })

  it('writes empty and single values as JSON.stringify does', () => {
    expect(canonicalJson({})).toBe('{}')
    expect(canonicalJson([])).toBe('[]')
    expect(canonicalJson({ a: {}, b: [] })).toBe('{\n  "a": {},\n  "b": []\n}')
    expect(canonicalJson(null)).toBe('null')
    expect(canonicalJson(undefined)).toBe('null')
    expect(canonicalJson('text')).toBe('"text"')
    expect(canonicalJson(1.5)).toBe('1.5')
    expect(canonicalJson(false)).toBe('false')
    // an undefined member is no member and an undefined item is null, as in JSON.stringify
    expect(canonicalJson({ a: undefined, b: [undefined, 1] })).toBe(JSON.stringify({ b: [null, 1] }, null, 2))
    expect(canonicalJson({ a: undefined })).toBe('{}')
  })

  it('is the same text for two documents whose members are in another order', () => {
    const one = parsed('{"a": 1, "b": {"x": [1, {"p": null, "q": "r"}], "y": true}, "c": []}')
    const other = parsed('{"c": [], "b": {"y": true, "x": [1, {"q": "r", "p": null}]}, "a": 1}')

    expect(JSON.stringify(one)).not.toBe(JSON.stringify(other))
    expect(canonicalJson(one)).toBe(canonicalJson(other))
  })

  it('tells null from a missing member, and a text from a number', () => {
    expect(canonicalJson({ a: null })).not.toBe(canonicalJson({}))
    expect(canonicalJson({ a: '' })).not.toBe(canonicalJson({ a: null }))
    expect(canonicalJson({ a: '1' })).not.toBe(canonicalJson({ a: 1 }))
    expect(canonicalJson({ a: [] })).not.toBe(canonicalJson({ a: {} }))
    expect(canonicalJson({ a: [null] })).not.toBe(canonicalJson({ a: [] }))
    expect(canonicalJson({ a: false })).not.toBe(canonicalJson({ a: 0 }))
  })

  it('keeps a member named __proto__, which a copy into a new object would lose', () => {
    const document = parsed('{"b": 1, "__proto__": {"polluted": true}, "a": {"__proto__": 2}}')
    const text = canonicalJson(document)

    expect(text).toBe(
      [
        '{',
        '  "__proto__": {',
        '    "polluted": true',
        '  },',
        '  "a": {',
        '    "__proto__": 2',
        '  },',
        '  "b": 1',
        '}',
      ].join('\n'),
    )
    expect(Object.getOwnPropertyDescriptor(parsed(text), '__proto__')?.value).toEqual({ polluted: true })
    // without the member the text is another one
    expect(text).not.toBe(canonicalJson(parsed('{"b": 1, "a": {"__proto__": 2}}')))
    // and nothing was written into the prototype of objects
    expect(({} as Record<string, unknown>).polluted).toBeUndefined()
  })

  it('keeps members named like the members of every object', () => {
    const document = parsed('{"toString": 1, "constructor": {"name": "x"}, "hasOwnProperty": null, "valueOf": []}')

    expect(parsed(canonicalJson(document))).toEqual(document)
    expect(Object.keys(parsed(canonicalJson(document)))).toEqual([
      'constructor',
      'hasOwnProperty',
      'toString',
      'valueOf',
    ])
  })

  it('puts names that look like numbers in the order of names, not of numbers', () => {
    const document = parsed('{"b": 1, "10": 2, "9": 3, "a": 4, "1e3": 5, "-1": 6, "007": 7, "1": 8, "": 9}')

    // an object lists whole numbers first and by value: the text must not depend on that
    expect(Object.keys(document).slice(0, 3)).toEqual(['1', '9', '10'])
    expect(canonicalJson(document)).toBe(
      [
        '{',
        '  "": 9,',
        '  "-1": 6,',
        '  "007": 7,',
        '  "1": 8,',
        '  "10": 2,',
        '  "1e3": 5,',
        '  "9": 3,',
        '  "a": 4,',
        '  "b": 1',
        '}',
      ].join('\n'),
    )
  })

  it('orders names by their UTF-16 units, the same in every browser and language', () => {
    const document = { z: 1, Z: 2, ä: 3, a: 4, _: 5, 'a b': 6, 'a.b': 7, 'a/b': 8, '😀': 9, ａ: 10 }

    expect(Object.keys(parsed(canonicalJson(document)))).toEqual([
      'Z',
      '_',
      'a',
      'a b',
      'a.b',
      'a/b',
      'z',
      'ä',
      '😀',
      'ａ',
    ])
  })

  it('escapes names and texts as JSON', () => {
    const document = {
      'a"b': 'quote " and backslash \\',
      'line\nbreak': 'tab\tand\r\nnew line',
      ' ': '  and \u0000 and 😀',
      lone: '\ud800',
    }
    const text = canonicalJson(document)

    expect(parsed(text)).toEqual(document)
    // one line per member: a line break inside a text never breaks the line of the diff
    expect(text.split('\n')).toHaveLength(6)
  })

  /** A random JSON value of every kind, with names and texts that need care. */
  function randomJson(next: () => number, names: readonly string[], depth = 0): unknown {
    const int = (below: number) => Math.floor(next() * below)
    const texts = ['', 'a', 'A', ' ', '"', '\\', 'line\nbreak', 'ž', '😀', ' ', '@config("/baseUrl")', '10']
    const kind = int(depth < 4 ? 8 : 6)
    switch (kind) {
      case 0:
        return null
      case 1:
        return next() < 0.5
      case 2:
        return int(2000) - 1000
      case 3:
        return (int(2_000_000) - 1_000_000) / 1000
      case 4:
        return [0, 1e21, 1e-7, 2 ** 53, -(2 ** 31), Number.MAX_VALUE, Number.MIN_VALUE][int(7)]
      case 5:
        return texts[int(texts.length)]
      case 6:
        return Array.from({ length: int(5) }, () => randomJson(next, names, depth + 1))
      default: {
        const entries = Array.from({ length: int(6) }, () => [names[int(names.length)]!, 0] as [string, unknown])
        // Object.fromEntries defines the members, so one named __proto__ is a member as after JSON.parse
        return Object.fromEntries(entries.map(([name]) => [name, randomJson(next, names, depth + 1)]))
      }
    }
  }

  /** The same value with the members of its objects in another order. */
  function shuffled(value: unknown, next: () => number): unknown {
    if (Array.isArray(value)) {
      return value.map((item) => shuffled(item, next))
    }
    if (value && typeof value === 'object') {
      const entries = Object.entries(value).map(([name, item]) => [name, shuffled(item, next)] as [string, unknown])
      for (let index = entries.length - 1; index > 0; index--) {
        const other = Math.floor(next() * (index + 1))
        ;[entries[index], entries[other]] = [entries[other]!, entries[index]!]
      }
      return Object.fromEntries(entries)
    }
    return value
  }

  /** A copy whose objects list their members in the order of their names; right for names that are no numbers. */
  function sortedCopy(value: unknown): unknown {
    if (Array.isArray(value)) {
      return value.map(sortedCopy)
    }
    if (value && typeof value === 'object') {
      const record = value as Record<string, unknown>
      return Object.fromEntries(
        Object.keys(record)
          .toSorted()
          .map((name) => [name, sortedCopy(record[name])]),
      )
    }
    return value
  }

  const hardNames = ['a', 'b', 'B', 'name', 'ä', '10', '9', '-1', '1e3', '', ' ', 'a.b', 'a/b', '~', '"q"', 'x\ny']
  const namesOfObject = ['__proto__', 'constructor', 'toString', '$patch', '$remove']

  it('parses back to the document it was written from, whatever its names and values', () => {
    const next = random(309)
    // the comparison of the test itself tells a member named __proto__
    expect(parsed('{"__proto__": 1}')).not.toEqual({})

    for (let round = 0; round < 300; round++) {
      const document = randomJson(next, [...hardNames, ...namesOfObject])
      const text = canonicalJson(document)

      expect(JSON.parse(text), text).toEqual(document)
      // so two documents have the same text only when they are the same document
      expect(canonicalJson(JSON.parse(text))).toBe(text)
    }
  })

  it('writes the same text for any order of the members', () => {
    const next = random(424)

    for (let round = 0; round < 300; round++) {
      const document = randomJson(next, [...hardNames, ...namesOfObject])

      expect(canonicalJson(shuffled(document, next))).toBe(canonicalJson(document))
    }
  })

  it('writes what JSON.stringify writes for a document whose members are in the order of their names', () => {
    const next = random(64)
    // names that an object keeps in the order they were added: no whole numbers, no __proto__
    const plain = ['a', 'b', 'B', 'name', 'ä', '-1', '1e3', '', ' ', 'a.b', 'a/b', '~', '"q"', 'x\ny', 'toString']

    for (let round = 0; round < 300; round++) {
      const document = randomJson(next, plain)

      expect(canonicalJson(document)).toBe(JSON.stringify(sortedCopy(document), null, 2))
    }
  })

  it('writes a deep and a long document', () => {
    // System.Text.Json reads 64 levels at most
    let deep: unknown = 'leaf'
    for (let level = 0; level < 64; level++) {
      deep = level % 2 ? [deep] : { z: deep, a: level }
    }
    expect(JSON.parse(canonicalJson(deep))).toEqual(deep)

    const long = Object.fromEntries(Array.from({ length: 50_000 }, (_, index) => [`name-${50_000 - index}`, index]))
    const text = canonicalJson(long)
    expect(text.split('\n')).toHaveLength(50_002)
    expect(text.startsWith('{\n  "name-1": 49999,\n  "name-10": 49990,')).toBe(true)
  })
})

describe('hasInexactNumber', () => {
  it('finds an integer beyond 2^53, which JSON.parse has rounded', () => {
    expect(hasInexactNumber(2 ** 53)).toBe(true)
    expect(hasInexactNumber(-(2 ** 53))).toBe(true)
    expect(hasInexactNumber(1e21)).toBe(true)
    // the server keeps 9007199254740993, the browser reads 9007199254740992
    expect(hasInexactNumber(JSON.parse('{"id": 9007199254740993}'))).toBe(true)
    expect(hasInexactNumber(JSON.parse('{"id": 12345678901234567890}'))).toBe(true)
  })

  it('takes every other number as exact', () => {
    for (const value of [0, -0, 1, -1, 2 ** 53 - 1, -(2 ** 53 - 1), 1.5, 0.1, 1e-7, 2 ** 53 + 0.5 - 2 ** 53]) {
      expect(hasInexactNumber(value), String(value)).toBe(false)
    }
  })

  it('takes a number too large for a double as inexact: JSON.parse reads it as Infinity', () => {
    expect(JSON.parse('{"a": 1e400}')).toEqual({ a: Number.POSITIVE_INFINITY })
    expect(hasInexactNumber(JSON.parse('{"a": 1e400}'))).toBe(true)
    expect(hasInexactNumber(JSON.parse('[-1e400]'))).toBe(true)
    // JSON writes it as null, so it must never compare equal to a document that has null there
    expect(hasInexactNumber({ a: null })).toBe(false)
  })

  it('looks into arrays and objects at every level', () => {
    expect(hasInexactNumber([1, [2, [2 ** 60]]])).toBe(true)
    expect(hasInexactNumber({ a: { b: [{ c: 2 ** 60 }] } })).toBe(true)
    expect(hasInexactNumber(parsed('{"__proto__": 9007199254740993}'))).toBe(true)
    expect(hasInexactNumber({ a: { b: [{ c: 2 ** 52 }] }, d: [1, 2, 3] })).toBe(false)
  })

  it('takes a text of digits, and anything that is no number, as exact', () => {
    expect(hasInexactNumber('9007199254740993')).toBe(false)
    expect(hasInexactNumber({ id: '9007199254740993', on: true, nothing: null })).toBe(false)
    expect(hasInexactNumber(null)).toBe(false)
    expect(hasInexactNumber(undefined)).toBe(false)
    expect(hasInexactNumber({})).toBe(false)
    expect(hasInexactNumber([])).toBe(false)
  })
})

describe('ownDocument', () => {
  it('is the document itself when it has members', () => {
    const content = { retries: 3 }

    expect(ownDocument(content)).toBe(content)
    expect(ownDocument({ a: null })).toEqual({ a: null })
    expect(ownDocument(parsed('{"__proto__": 1}'))).toBeDefined()
  })

  it('is none for a document without members, as "Create empty" stores it', () => {
    expect(ownDocument({})).toBeUndefined()
  })

  it('is none for anything that is no object', () => {
    for (const content of [undefined, null, '', 'text', 0, 1, true, [], [1], [{ a: 1 }]]) {
      expect(ownDocument(content), JSON.stringify(content)).toBeUndefined()
    }
  })
})

describe('countOf', () => {
  it('names one and several annotations and configurations', () => {
    expect(countOf('annotations', 1)).toBe('1 annotation')
    expect(countOf('annotations', 3)).toBe('3 annotations')
    expect(countOf('annotations', 0)).toBe('0 annotations')
    expect(countOf('configurations', 1)).toBe('1 configuration')
    expect(countOf('configurations', 100)).toBe('100 configurations')
  })
})

/** The paths and queries of the requests from now on. */
function recordRequests() {
  const requests: string[] = []
  server.events.on('request:start', ({ request }) => {
    const url = new URL(request.url)
    requests.push(`${request.method} ${url.pathname}${url.search}`)
  })
  return requests
}

const annotationsUrl = '/api/v1/acme/billing/default/annotations'
const configurationUrl = '/api/v1/acme/billing/default/configuration'

describe('readAnnotation', () => {
  it('reads a key of the view with one request, inactive annotations included', async () => {
    const requests = recordRequests()

    const side = await readAnnotation(scope, 'sbt.northwind')

    expect(side.deleted).toBe(false)
    expect(side.annotation).toMatchObject({ AnnotationKey: 'sbt.northwind', ViewName: 'default', Name: 'northwind' })
    expect(requests).toEqual([`GET ${annotationsUrl}/sbt.northwind?includeInactive=true`])
  })

  it('reads the view it is asked for', async () => {
    // sbt.initech is a subject of default only, and sbt.northwind has another title in each view
    expect((await readAnnotation(scope, 'sbt.initech')).annotation?.AnnotationKey).toBe('sbt.initech')
    expect(await readAnnotation(staging, 'sbt.initech')).toEqual({ deleted: false })
    expect((await readAnnotation(staging, 'sbt.northwind')).annotation?.ViewName).toBe('staging')
  })

  it('takes a key that the view does not have as no annotation, not as an error', async () => {
    const side = await readAnnotation(scope, 'sbt.nobody')

    expect(side).toEqual({ deleted: false })
    expect('annotation' in side).toBe(false)
  })

  it('reads a disabled, an expired and a scheduled annotation', async () => {
    await setAnnotation(org, project, view, 'sbt.disabled', subject('disabled', { IsDisabled: true }))
    await setAnnotation(org, project, view, 'sbt.expired', subject('expired', { ExpiresAt: '2020-01-01T00:00:00Z' }))
    await setAnnotation(
      org,
      project,
      view,
      'sbt.scheduled',
      subject('scheduled', { ValidFrom: '2999-01-01T00:00:00Z' }),
    )
    // the plain read of the API does not answer them
    await expect(getAnnotation(org, project, view, 'sbt.disabled')).rejects.toMatchObject({
      status: 404,
      errorCode: 'AnnotationInactive',
    })

    for (const name of ['disabled', 'expired', 'scheduled']) {
      const side = await readAnnotation(scope, `sbt.${name}`)
      expect(side.deleted, name).toBe(false)
      expect(side.annotation?.AnnotationKey, name).toBe(`sbt.${name}`)
    }
    expect((await readAnnotation(scope, 'sbt.disabled')).annotation?.IsDisabled).toBe(true)
  })

  it('does not tell a deleted key from a missing one without includeDeleted', async () => {
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    const requests = recordRequests()

    expect(await readAnnotation(scope, 'sbt.contoso')).toEqual({ deleted: false })
    expect(await readAnnotation(scope, 'sbt.contoso', { includeDeleted: false })).toEqual({ deleted: false })
    expect(requests).toEqual([
      `GET ${annotationsUrl}/sbt.contoso?includeInactive=true`,
      `GET ${annotationsUrl}/sbt.contoso?includeInactive=true`,
    ])
  })

  it('tells a deleted key with includeDeleted, and answers no annotation for it', async () => {
    await deleteAnnotation(org, project, view, 'sbt.contoso')
    const requests = recordRequests()

    const side = await readAnnotation(scope, 'sbt.contoso', { includeDeleted: true })

    expect(side).toEqual({ deleted: true })
    expect('annotation' in side).toBe(false)
    expect(requests).toEqual([`GET ${annotationsUrl}/sbt.contoso?includeInactive=true&includeDeleted=true`])
    // the descendants were deleted with it
    expect(await readAnnotation(scope, 'cnt.contoso.prod', { includeDeleted: true })).toEqual({ deleted: true })
    // and with includeDeleted, a key of the view and a missing one read as without it
    expect((await readAnnotation(scope, 'sbt.northwind', { includeDeleted: true })).annotation).toBeDefined()
    expect(await readAnnotation(scope, 'sbt.nobody', { includeDeleted: true })).toEqual({ deleted: false })
  })

  it('tells a deleted key that was disabled', async () => {
    await setAnnotation(org, project, view, 'sbt.disabled', subject('disabled', { IsDisabled: true }))
    await deleteAnnotation(org, project, view, 'sbt.disabled')

    expect(await readAnnotation(scope, 'sbt.disabled', { includeDeleted: true })).toEqual({ deleted: true })
    expect(await readAnnotation(scope, 'sbt.disabled')).toEqual({ deleted: false })
  })

  it('throws every error but a 404', async () => {
    // the mock user is no member of the exhibition
    await expect(
      readAnnotation({ org: 'exhibition', project: 'exhibition', view: 'default' }, 'sbt.northwind'),
    ).rejects.toMatchObject({ status: 403, errorCode: 'AccessDenied' })

    server.use(
      http.get('*/api/v1/:org/:project/:view/annotations/:key', () =>
        HttpResponse.json({ Message: 'The database is busy.' }, { status: 503 }),
      ),
    )
    await expect(readAnnotation(scope, 'sbt.northwind')).rejects.toMatchObject({
      status: 503,
      message: 'The database is busy.',
    })

    server.use(http.get('*/api/v1/:org/:project/:view/annotations/:key', () => HttpResponse.error()))
    await expect(readAnnotation(scope, 'sbt.northwind')).rejects.toMatchObject({ status: 0 })
  })

  it('passes the signal on: an aborted read rejects as aborted', async () => {
    const before = await readAnnotation(scope, 'sbt.northwind', {
      signal: AbortSignal.abort(),
      includeDeleted: true,
    }).catch((error: unknown) => error)
    expect(isAborted(before)).toBe(true)

    // aborted while the answer never comes: without the signal, the read would not end
    const controller = new AbortController()
    server.use(
      http.get('*/api/v1/:org/:project/:view/annotations/:key', async () => {
        controller.abort()
        await delay('infinite')
      }),
    )
    const during = await readAnnotation(scope, 'sbt.northwind', { signal: controller.signal }).catch(
      (error: unknown) => error,
    )
    expect(isAborted(during)).toBe(true)
  })
})

describe('readConfiguration', () => {
  /** A new subject of the view: the mock gives an annotation that is created later no configuration. */
  async function createSubject(name: string, scopeOf = scope) {
    await setAnnotation(scopeOf.org, scopeOf.project, scopeOf.view, `sbt.${name}`, {
      ...subject(name),
      ViewName: scopeOf.view,
    })
    return `sbt.${name}`
  }

  it('reads the effective document, then the own document of its version', async () => {
    const key = await createSubject('fresh')
    const own = { region: 'eu-west', limits: { soft: 10 } }
    await setConfiguration(org, project, view, key, own)
    const requests = recordRequests()

    const side = await readConfiguration(scope, key)

    expect(side).toEqual({ readable: true, deleted: false, version: 1, effective: own, own })
    expect(requests).toEqual([`GET ${configurationUrl}/${key}`, `GET ${configurationUrl}/${key}/versions/1`])
  })

  it('reads the own document apart from what the key inherits', async () => {
    await createSubject('fresh')
    await setConfiguration(org, project, view, 'sbt.fresh', { region: 'eu-west', baseUrl: 'https://fresh.example' })
    await setAnnotation(org, project, view, 'cnt.fresh.prod', {
      ...subject('fresh'),
      AnnotationKey: 'cnt.fresh.prod',
      Name: 'prod',
      AnnotationType: 'Context',
      SubjectKey: 'sbt.fresh',
      SubjectName: 'fresh',
    } as Annotation)
    await setConfiguration(org, project, view, 'cnt.fresh.prod', { baseUrl: 'https://prod.fresh.example' })

    expect(await readConfiguration(scope, 'cnt.fresh.prod')).toEqual({
      readable: true,
      deleted: false,
      version: 1,
      effective: { region: 'eu-west', baseUrl: 'https://prod.fresh.example' },
      own: { baseUrl: 'https://prod.fresh.example' },
    })
  })

  it('reads the last version of a configuration that was written several times', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, key, { region: 'eu-west' })
    await setConfiguration(org, project, view, key, { retries: 3 })

    // a write merges into the stored document
    expect(await readConfiguration(scope, key)).toMatchObject({ version: 2, own: { region: 'eu-west', retries: 3 } })
  })

  it('answers a key deleted between its two reads as deleted, not with the effective document read before', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, key, { region: 'eu-west' })
    server.use(
      http.get(`*${configurationUrl}/${key}/versions/1`, () =>
        HttpResponse.json(
          { Message: `The annotation ${key} is deleted.`, ErrorCode: 'AnnotationDeleted' },
          { status: 404 },
        ),
      ),
    )

    expect(await readConfiguration(scope, key)).toEqual({ readable: false, deleted: true, version: 0 })
  })

  it('answers no own document for a key without one, and asks for version 0', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, 'sbt.northwind', { region: 'eu-west' })
    await setAnnotation(org, project, view, 'cnt.northwind.fresh', {
      ...subject('northwind'),
      AnnotationKey: 'cnt.northwind.fresh',
      Name: 'fresh',
      AnnotationType: 'Context',
      SubjectKey: 'sbt.northwind',
      SubjectName: 'northwind',
    } as Annotation)
    const requests = recordRequests()

    const side = await readConfiguration(scope, key)
    expect(side).toEqual({ readable: true, deleted: false, version: 0, effective: {} })
    expect(side.own).toBeUndefined()
    expect(requests).toEqual([`GET ${configurationUrl}/${key}`, `GET ${configurationUrl}/${key}/versions/0`])

    // what it inherits is its effective document, and still no own one
    const inheriting = await readConfiguration(scope, 'cnt.northwind.fresh')
    expect(inheriting).toMatchObject({ readable: true, deleted: false, version: 0, effective: { region: 'eu-west' } })
    expect(inheriting.own).toBeUndefined()
  })

  it('answers no own document after the configuration was deleted', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, key, { region: 'eu-west' })
    await deleteConfiguration(org, project, view, key)

    const side = await readConfiguration(scope, key)

    expect(side).toEqual({ readable: true, deleted: false, version: 0, effective: {} })
    expect(side.own).toBeUndefined()
  })

  it('takes a stored document without members as no own document, and keeps its version', async () => {
    const key = await createSubject('fresh')
    // the editor's "Create empty"
    await setConfiguration(org, project, view, key, {})

    const side = await readConfiguration(scope, key)

    expect(side).toEqual({ readable: true, deleted: false, version: 1, effective: {} })
    expect(side.own).toBeUndefined()
  })

  it('answers a key without an annotation as not readable, not deleted', async () => {
    const requests = recordRequests()

    expect(await readConfiguration(scope, 'sbt.nobody')).toEqual({ readable: false, deleted: false, version: 0 })
    // the own document is not asked for
    expect(requests).toEqual([`GET ${configurationUrl}/sbt.nobody`])
    // exe.northwind.reporting.test is an execution of default only
    expect((await readConfiguration(scope, 'exe.northwind.reporting.test')).readable).toBe(true)
    expect(await readConfiguration(staging, 'exe.northwind.reporting.test')).toEqual({
      readable: false,
      deleted: false,
      version: 0,
    })
  })

  it('answers a key whose annotation is deleted as not readable and deleted', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, key, { region: 'eu-west' })
    await deleteAnnotation(org, project, view, key)
    // the mock answers the code of the API
    await expect(getConfiguration(org, project, view, key)).rejects.toMatchObject({
      status: 404,
      errorCode: 'AnnotationDeleted',
    })
    const requests = recordRequests()

    expect(await readConfiguration(scope, key)).toEqual({ readable: false, deleted: true, version: 0 })
    expect(requests).toEqual([`GET ${configurationUrl}/${key}`])
  })

  it('answers the descendant of a deleted annotation as deleted too', async () => {
    await deleteAnnotation(org, project, view, 'sbt.contoso')

    expect(await readConfiguration(scope, 'cnt.contoso.prod')).toEqual({ readable: false, deleted: true, version: 0 })
    expect(await readConfiguration(staging, 'cnt.contoso.prod')).toMatchObject({ readable: true, deleted: false })
  })

  it('reads the view it is asked for', async () => {
    await createSubject('fresh')
    await createSubject('fresh', staging)
    await setConfiguration(org, project, view, 'sbt.fresh', { region: 'eu-west' })
    await setConfiguration(org, project, 'staging', 'sbt.fresh', { region: 'us-east', retries: 1 })

    expect((await readConfiguration(scope, 'sbt.fresh')).own).toEqual({ region: 'eu-west' })
    expect((await readConfiguration(staging, 'sbt.fresh')).own).toEqual({ region: 'us-east', retries: 1 })
  })

  it('throws every error but a 404 of the effective document', async () => {
    await expect(
      readConfiguration({ org: 'exhibition', project: 'exhibition', view: 'default' }, 'sbt.northwind'),
    ).rejects.toMatchObject({ status: 403, errorCode: 'AccessDenied' })

    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key', () =>
        HttpResponse.json({ Message: 'The database is busy.' }, { status: 503 }),
      ),
    )
    await expect(readConfiguration(scope, 'sbt.northwind')).rejects.toMatchObject({ status: 503 })
  })

  it('throws every error but a 404 of the own document', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, key, { region: 'eu-west' })
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key/versions/:version', () =>
        HttpResponse.json({ Message: 'The database is busy.' }, { status: 503 }),
      ),
    )

    await expect(readConfiguration(scope, key)).rejects.toMatchObject({
      status: 503,
      message: 'The database is busy.',
    })
  })

  it('passes the signal on to both reads', async () => {
    const key = await createSubject('fresh')
    await setConfiguration(org, project, view, key, { region: 'eu-west' })
    const before = await readConfiguration(scope, key, { signal: AbortSignal.abort() }).catch((error: unknown) => error)
    expect(isAborted(before)).toBe(true)

    // Each read is aborted while its answer never comes: without the signal, the read would not end.
    const effective = new AbortController()
    let requests = recordRequests()
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key', async () => {
        effective.abort()
        await delay('infinite')
      }),
    )
    const first = await readConfiguration(scope, key, { signal: effective.signal }).catch((error: unknown) => error)
    expect(isAborted(first)).toBe(true)
    // the own document is not asked for after that
    expect(requests).toEqual([`GET ${configurationUrl}/${key}`])

    server.events.removeAllListeners()
    server.resetHandlers()
    const own = new AbortController()
    requests = recordRequests()
    server.use(
      http.get('*/api/v1/:org/:project/:view/configuration/:key/versions/:version', async () => {
        own.abort()
        await delay('infinite')
      }),
    )
    const second = await readConfiguration(scope, key, { signal: own.signal }).catch((error: unknown) => error)
    expect(isAborted(second)).toBe(true)
    expect(requests).toEqual([`GET ${configurationUrl}/${key}`, `GET ${configurationUrl}/${key}/versions/1`])
  })
})
