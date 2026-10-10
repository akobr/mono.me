import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import {
  compileFilter,
  parseFilter,
  removeTerm,
  setQualifier,
  tokenize,
  type FilterContext,
} from '@/utils/filter-parser'
import { getAllAncestors, parseAnnotationKey } from '@/utils/annotation-key'

const annotation = (key: string, extra: Partial<Annotation> = {}): Annotation => ({
  ProjectName: 'billing',
  ViewName: 'default',
  AnnotationKey: key,
  Name: key.split('.').at(-1)!,
  AnnotationType: 'Execution',
  ...extra,
})

const now = Date.parse('2026-10-10T12:00:00Z')
const context: FilterContext = {
  now,
  configured: new Set(['exe.northwind.invoicing.prod']),
  ancestorsOf: getAllAncestors,
  partsOf: parseAnnotationKey,
}

const data = [
  annotation('rst.invoicing', { Labels: ['eu'] }),
  annotation('sbt.northwind', { Title: 'Late fee rules' }),
  annotation('exe.northwind.invoicing.prod', { Labels: ['eu', 'pci'], Description: 'Applies the late fee' }),
  annotation('exe.contoso.payments.staging', { Labels: ['us'], IsDisabled: true }),
  annotation('uxe.northwind.invoicing.prod.pdf', { ValidFrom: '2026-11-01T00:00:00Z', Values: { tier: 'gold' } }),
  annotation('cnt.contoso.eu', { ExpiresAt: '2026-01-01T00:00:00Z', DocumentationLink: 'https://docs.test' }),
]

const run = (query: string) =>
  data.filter(compileFilter(parseFilter(query), context)).map((entry) => entry.AnnotationKey)

describe('tokenize and parseFilter', () => {
  it('keeps quoted spaces together', () => {
    expect(tokenize('type:exe "late fee" label:"two words",eu')).toEqual([
      'type:exe',
      '"late fee"',
      'label:"two words",eu',
    ])
  })

  it('parses qualifiers, negation, OR values and free text', () => {
    const parsed = parseFilter('type:exe label:eu,us -is:disabled "late fee"')

    expect(parsed.terms).toEqual([
      { raw: 'type:exe', negated: false, qualifier: 'type', values: ['exe'], error: undefined },
      { raw: 'label:eu,us', negated: false, qualifier: 'label', values: ['eu', 'us'], error: undefined },
      { raw: '-is:disabled', negated: true, qualifier: 'is', values: ['disabled'], error: undefined },
      { raw: '"late fee"', negated: false, values: ['late fee'], error: undefined },
    ])
    expect(parsed.errors).toEqual([])
  })

  it('reports invalid values and unknown qualifiers', () => {
    const parsed = parseFilter('type:foo is:nope colour:red has:')

    expect(parsed.errors).toEqual([
      'Unknown type foo.',
      'is: accepts active, disabled, scheduled, expired.',
      'Unknown qualifier colour: (searched as text).',
      'has: needs a value.',
    ])
  })
})

describe('compileFilter', () => {
  it('matches types by code or name, and labels as OR', () => {
    expect(run('type:execution label:eu,us')).toEqual(['exe.northwind.invoicing.prod', 'exe.contoso.payments.staging'])
  })

  it('excludes negated terms', () => {
    expect(run('type:exe -is:disabled')).toEqual(['exe.northwind.invoicing.prod'])
  })

  it('computes the status with the current time', () => {
    expect(run('is:scheduled')).toEqual(['uxe.northwind.invoicing.prod.pdf'])
    expect(run('is:expired')).toEqual(['cnt.contoso.eu'])
  })

  it('evaluates has: against the catalog and the fields', () => {
    expect(run('has:config')).toEqual(['exe.northwind.invoicing.prod'])
    expect(run('has:values')).toEqual(['uxe.northwind.invoicing.prod.pdf'])
    expect(run('has:docs,description')).toEqual(['exe.northwind.invoicing.prod', 'cnt.contoso.eu'])
  })

  it('matches key parts, including the own name of subjects and responsibilities', () => {
    expect(run('subject:northwind')).toEqual([
      'sbt.northwind',
      'exe.northwind.invoicing.prod',
      'uxe.northwind.invoicing.prod.pdf',
    ])
    expect(run('responsibility:invoicing type:rst,exe')).toEqual(['rst.invoicing', 'exe.northwind.invoicing.prod'])
    expect(run('context:eu')).toEqual(['cnt.contoso.eu'])
    expect(run('unit:pdf')).toEqual(['uxe.northwind.invoicing.prod.pdf'])
  })

  it('matches keys by prefix or glob, and descendants by ancestor', () => {
    expect(run('key:exe.north')).toEqual(['exe.northwind.invoicing.prod'])
    expect(run('key:*.contoso.*')).toEqual(['exe.contoso.payments.staging', 'cnt.contoso.eu'])
    expect(run('ancestor:rst.invoicing')).toEqual(['exe.northwind.invoicing.prod', 'uxe.northwind.invoicing.prod.pdf'])
  })

  it('searches name, key, title and description case-insensitively', () => {
    expect(run('"late fee"')).toEqual(['sbt.northwind', 'exe.northwind.invoicing.prod'])
    expect(run('LATE contoso')).toEqual([])
  })

  it('ignores qualifier terms with errors and passes everything for an empty query', () => {
    expect(run('type:foo')).toHaveLength(data.length)
    expect(run('')).toHaveLength(data.length)
  })
})

describe('editing the query', () => {
  it('removes a chip and replaces a qualifier', () => {
    const parsed = parseFilter('type:exe label:eu -is:disabled')

    expect(removeTerm(parsed, 1)).toBe('type:exe -is:disabled')
    expect(setQualifier('type:exe label:eu', 'type', ['sbt', 'unit of work'])).toBe('label:eu type:sbt,"unit of work"')
    expect(setQualifier('type:exe label:eu', 'type', [])).toBe('label:eu')
  })
})
