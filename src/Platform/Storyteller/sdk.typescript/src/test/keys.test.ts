import { describe, expect, it } from 'vitest'
import {
  annotationTypes,
  buildAnnotationKey,
  getAllAncestors,
  getAnnotationTypeInfo,
  getDirectAncestors,
  isValidAnnotationKey,
  isValidName,
  parseAnnotationKey,
} from '../lib/keys'

describe('parseAnnotationKey', () => {
  it.each([
    ['rst.invoicing', { type: 'Responsibility', responsibility: 'invoicing', name: 'invoicing' }],
    ['sbt.northwind', { type: 'Subject', subject: 'northwind', name: 'northwind' }],
    ['unt.invoicing.pdf', { type: 'Unit', responsibility: 'invoicing', unit: 'pdf', name: 'pdf' }],
    ['cnt.northwind.prod', { type: 'Context', subject: 'northwind', context: 'prod', name: 'prod' }],
    ['usg.northwind.invoicing', { type: 'Usage', subject: 'northwind', responsibility: 'invoicing', name: 'invoicing' }],
    ['exe.northwind.invoicing.prod', { type: 'Execution', subject: 'northwind', responsibility: 'invoicing', context: 'prod' }],
    ['uxe.northwind.invoicing.prod.pdf', { type: 'UnitOfExecution', subject: 'northwind', responsibility: 'invoicing', context: 'prod', unit: 'pdf' }],
  ])('parses %s', (key, expected) => {
    expect(parseAnnotationKey(key)).toMatchObject({ key, ...expected })
  })

  it.each(['', 'rst', 'rst.a.b', 'xyz.a', 'exe.a.b', 'uxe.a.b.c.d.e', 'unt..pdf', 'sbt.'])('rejects %j', (key) => {
    expect(parseAnnotationKey(key)).toBeUndefined()
    expect(isValidAnnotationKey(key)).toBe(false)
  })

  it('matches the segment counts of AnnotationKeyExtensions.IsValid', () => {
    expect(Object.fromEntries(annotationTypes.map((info) => [info.code, info.segments]))).toEqual({
      rst: 2,
      sbt: 2,
      unt: 3,
      usg: 3,
      cnt: 3,
      exe: 4,
      uxe: 5,
    })
  })

  it('accepts the type code in any case, like the server', () => {
    expect(parseAnnotationKey('EXE.s.r.c')?.code).toBe('exe')
  })
})

describe('buildAnnotationKey', () => {
  it('builds every type from its parts', () => {
    const parts = { subject: 'northwind', responsibility: 'invoicing', context: 'prod', unit: 'pdf' }

    expect(buildAnnotationKey('rst', { name: 'invoicing' })).toBe('rst.invoicing')
    expect(buildAnnotationKey('Subject', { name: 'northwind' })).toBe('sbt.northwind')
    expect(buildAnnotationKey('unt', parts)).toBe('unt.invoicing.pdf')
    expect(buildAnnotationKey('cnt', parts)).toBe('cnt.northwind.prod')
    expect(buildAnnotationKey('usg', parts)).toBe('usg.northwind.invoicing')
    expect(buildAnnotationKey('Execution', parts)).toBe('exe.northwind.invoicing.prod')
    expect(buildAnnotationKey('uxe', parts)).toBe('uxe.northwind.invoicing.prod.pdf')
  })

  it('rejects missing parts and names with dots', () => {
    expect(() => buildAnnotationKey('exe', { subject: 's', responsibility: 'r' })).toThrow(/context/)
    expect(() => buildAnnotationKey('rst', { name: 'a.b' })).toThrow(/responsibility/)
    expect(() => buildAnnotationKey('xyz' as never, {})).toThrow(/Unknown/)
  })
})

describe('ancestors', () => {
  it('returns the direct ancestors in merge order (inheritance.md)', () => {
    expect(getDirectAncestors('rst.r')).toEqual([])
    expect(getDirectAncestors('sbt.s')).toEqual([])
    expect(getDirectAncestors('unt.r.u')).toEqual(['rst.r'])
    expect(getDirectAncestors('cnt.s.c')).toEqual(['sbt.s'])
    expect(getDirectAncestors('usg.s.r')).toEqual(['rst.r', 'sbt.s'])
    expect(getDirectAncestors('exe.s.r.c')).toEqual(['usg.s.r', 'cnt.s.c'])
    expect(getDirectAncestors('uxe.s.r.c.u')).toEqual(['unt.r.u', 'exe.s.r.c'])
    expect(getDirectAncestors('invalid')).toEqual([])
  })

  it('returns every ancestor once, level by level', () => {
    expect(getAllAncestors('uxe.s.r.c.u')).toEqual(['unt.r.u', 'exe.s.r.c', 'rst.r', 'usg.s.r', 'cnt.s.c', 'sbt.s'])
  })
})

describe('names and types', () => {
  it('validates names used inside keys', () => {
    expect(isValidName('northwind')).toBe(true)
    expect(isValidName('')).toBe(false)
    expect(isValidName(' padded ')).toBe(false)
    expect(isValidName('a.b')).toBe(false)
  })

  it('resolves type infos by code and by name', () => {
    expect(getAnnotationTypeInfo('uxe')?.type).toBe('UnitOfExecution')
    expect(getAnnotationTypeInfo('Context')?.code).toBe('cnt')
    expect(getAnnotationTypeInfo('nope')).toBeUndefined()
  })
})
