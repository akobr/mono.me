import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { annotationStatus } from '@/utils/annotation-status'
import { expandableIds, keySelectionOf, projectTree, rowSelectionOf, type TreeNode } from '@/utils/tree-projection'
import {
  canDescend,
  codeOfKey,
  deletedWith,
  descendantTypeCodes,
  metaOf,
  typeCodes,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { labelColor, normalizeLabels } from '@/utils/labels'

const annotation = (key: string): Annotation => ({
  ProjectName: 'p',
  ViewName: 'default',
  AnnotationKey: key,
  Name: key.split('.').at(-1)!,
  AnnotationType: 'Execution',
})

describe('annotationStatus', () => {
  const now = Date.parse('2026-10-10T12:00:00Z')

  it('prefers disabled, then scheduled, then expired', () => {
    expect(annotationStatus({ IsDisabled: true, ValidFrom: '2027-01-01T00:00:00Z' }, now)).toBe('disabled')
    expect(annotationStatus({ ValidFrom: '2026-10-10T12:00:01Z', ExpiresAt: '2020-01-01T00:00:00Z' }, now)).toBe(
      'scheduled',
    )
    expect(annotationStatus({ ExpiresAt: '2026-10-10T12:00:00Z' }, now)).toBe('expired')
    expect(annotationStatus({ ValidFrom: '2026-10-10T12:00:00Z', ExpiresAt: '2026-10-10T12:00:01Z' }, now)).toBe(
      'active',
    )
  })

  it('ignores invalid dates and offsets are honored', () => {
    expect(annotationStatus({ ValidFrom: 'not a date' }, now)).toBe('active')
    expect(annotationStatus({ ExpiresAt: '2026-10-10T13:00:00+02:00' }, now)).toBe('expired')
  })
})

describe('projectTree', () => {
  const keys = [
    'rst.invoicing',
    'sbt.northwind',
    'cnt.northwind.prod',
    'usg.northwind.invoicing',
    'exe.northwind.invoicing.prod',
    'unt.invoicing.pdf',
    'uxe.northwind.invoicing.prod.pdf',
    'uxe.contoso.invoicing.eu.pdf',
  ]
  const data = keys.map(annotation)

  const shape = (nodes: TreeNode[]): unknown[] =>
    nodes.map((node) =>
      node.children.length
        ? { [node.missing ? `(missing) ${node.key}` : node.key]: shape(node.children) }
        : node.missing
          ? `(missing) ${node.key}`
          : node.key,
    )

  const flatten = (roots: TreeNode[]): TreeNode[] => {
    const flat: TreeNode[] = []
    const walk = (node: TreeNode) => (flat.push(node), node.children.forEach(walk))
    roots.forEach(walk)
    return flat
  }

  it('shows only subjects at the root, with every path to the descendants and missing parents as placeholders', () => {
    const northwindExecution = { 'exe.northwind.invoicing.prod': ['uxe.northwind.invoicing.prod.pdf'] }
    const contosoExecution = { '(missing) exe.contoso.invoicing.eu': ['uxe.contoso.invoicing.eu.pdf'] }

    expect(shape(projectTree(data, 'subject'))).toEqual([
      {
        '(missing) sbt.contoso': [
          { '(missing) cnt.contoso.eu': [contosoExecution] },
          { '(missing) usg.contoso.invoicing': [contosoExecution] },
        ],
      },
      {
        'sbt.northwind': [
          { 'cnt.northwind.prod': [northwindExecution] },
          { 'usg.northwind.invoicing': [northwindExecution] },
        ],
      },
    ])
  })

  it('shows only responsibilities at the root, with units and usages and every path below them', () => {
    expect(shape(projectTree(data, 'responsibility'))).toEqual([
      {
        'rst.invoicing': [
          { 'unt.invoicing.pdf': ['uxe.contoso.invoicing.eu.pdf', 'uxe.northwind.invoicing.prod.pdf'] },
          {
            '(missing) usg.contoso.invoicing': [
              { '(missing) exe.contoso.invoicing.eu': ['uxe.contoso.invoicing.eu.pdf'] },
            ],
          },
          {
            'usg.northwind.invoicing': [{ 'exe.northwind.invoicing.prod': ['uxe.northwind.invoicing.prod.pdf'] }],
          },
        ],
      },
    ])
  })

  it('gives every row a unique id, the path of keys from the root', () => {
    const flat = flatten(projectTree(data, 'subject'))

    expect(new Set(flat.map((node) => node.id)).size).toBe(flat.length)
    expect(
      flat.filter((node) => node.key === 'uxe.northwind.invoicing.prod.pdf').map((node) => [node.id, node.depth]),
    ).toEqual([
      ['sbt.northwind/cnt.northwind.prod/exe.northwind.invoicing.prod/uxe.northwind.invoicing.prod.pdf', 3],
      ['sbt.northwind/usg.northwind.invoicing/exe.northwind.invoicing.prod/uxe.northwind.invoicing.prod.pdf', 3],
    ])
  })

  it('keeps every path to a match and marks its ancestors as not matched', () => {
    const roots = projectTree(data, 'subject', (entry) => entry.AnnotationKey === 'uxe.northwind.invoicing.prod.pdf')

    expect(flatten(roots).map((node) => [node.key, node.matched, node.depth])).toEqual([
      ['sbt.northwind', false, 0],
      ['cnt.northwind.prod', false, 1],
      ['exe.northwind.invoicing.prod', false, 2],
      ['uxe.northwind.invoicing.prod.pdf', true, 3],
      ['usg.northwind.invoicing', false, 1],
      ['exe.northwind.invoicing.prod', false, 2],
      ['uxe.northwind.invoicing.prod.pdf', true, 3],
    ])
    expect(expandableIds(roots)).toEqual([
      'sbt.northwind',
      'sbt.northwind/cnt.northwind.prod',
      'sbt.northwind/cnt.northwind.prod/exe.northwind.invoicing.prod',
      'sbt.northwind/usg.northwind.invoicing',
      'sbt.northwind/usg.northwind.invoicing/exe.northwind.invoicing.prod',
    ])
  })

  it('leaves out the types that are not below the roots of the perspective', () => {
    const byType = (code: string) => (entry: Annotation) => entry.AnnotationKey.startsWith(`${code}.`)

    expect(projectTree(data, 'subject', byType('rst'))).toEqual([])
    expect(projectTree(data, 'subject', byType('unt'))).toEqual([])
    expect(projectTree(data, 'responsibility', byType('sbt'))).toEqual([])
    expect(projectTree(data, 'responsibility', byType('cnt'))).toEqual([])
  })

  it('selects an annotation on every row that shows it', () => {
    const rows = flatten(projectTree(data, 'subject'))
    const contextCopy = 'sbt.northwind/cnt.northwind.prod/exe.northwind.invoicing.prod'
    const usageCopy = 'sbt.northwind/usg.northwind.invoicing/exe.northwind.invoicing.prod'

    const selected = keySelectionOf(rows, { 'rst.invoicing': true }, {}, { [contextCopy]: true })
    expect(selected).toEqual({ 'rst.invoicing': true, 'exe.northwind.invoicing.prod': true })

    const shown = rowSelectionOf(rows, selected)
    expect(shown).toEqual({ [contextCopy]: true, [usageCopy]: true })

    // Clearing one copy clears the annotation; the key of a row that is not shown stays selected.
    expect(keySelectionOf(rows, selected, shown, { [contextCopy]: true })).toEqual({ 'rst.invoicing': true })
  })
})

describe('deletedWith', () => {
  const keys = [
    'rst.invoicing',
    'unt.invoicing.worker',
    'sbt.northwind',
    'cnt.northwind.prod',
    'usg.northwind.invoicing',
    'exe.northwind.invoicing.prod',
    'uxe.northwind.invoicing.prod.worker',
    'usg.contoso.invoicing',
    'exe.contoso.invoicing.prod',
  ]

  it('takes every descendant in the inheritance graph, for all seven types', () => {
    expect(deletedWith('rst.invoicing', keys).toSorted()).toEqual(
      [
        'unt.invoicing.worker',
        'usg.northwind.invoicing',
        'exe.northwind.invoicing.prod',
        'uxe.northwind.invoicing.prod.worker',
        'usg.contoso.invoicing',
        'exe.contoso.invoicing.prod',
      ].toSorted(),
    )
    expect(deletedWith('sbt.northwind', keys).toSorted()).toEqual(
      [
        'cnt.northwind.prod',
        'usg.northwind.invoicing',
        'exe.northwind.invoicing.prod',
        'uxe.northwind.invoicing.prod.worker',
      ].toSorted(),
    )
    expect(deletedWith('usg.northwind.invoicing', keys).toSorted()).toEqual(
      ['exe.northwind.invoicing.prod', 'uxe.northwind.invoicing.prod.worker'].toSorted(),
    )
    expect(deletedWith('unt.invoicing.worker', keys)).toEqual(['uxe.northwind.invoicing.prod.worker'])
    // A context takes its executions and their units of execution; an execution takes its units of execution.
    expect(deletedWith('cnt.northwind.prod', keys).toSorted()).toEqual(
      ['exe.northwind.invoicing.prod', 'uxe.northwind.invoicing.prod.worker'].toSorted(),
    )
    expect(deletedWith('exe.northwind.invoicing.prod', keys)).toEqual(['uxe.northwind.invoicing.prod.worker'])
    expect(deletedWith('uxe.northwind.invoicing.prod.worker', keys)).toEqual([])
  })
})

describe('descendant types', () => {
  // The table of DescendantTypeRulesTests (Backend.Core), in typeCodes order, so the UI and the service agree.
  const below: Record<AnnotationTypeCode, AnnotationTypeCode[]> = {
    rst: ['unt', 'usg', 'exe', 'uxe'],
    unt: ['uxe'],
    sbt: ['usg', 'cnt', 'exe', 'uxe'],
    usg: ['exe', 'uxe'],
    cnt: ['exe', 'uxe'],
    exe: ['uxe'],
    uxe: [],
  }
  const sample: Record<AnnotationTypeCode, string> = {
    rst: 'rst.invoicing',
    unt: 'unt.invoicing.worker',
    sbt: 'sbt.northwind',
    usg: 'usg.northwind.invoicing',
    cnt: 'cnt.northwind.prod',
    exe: 'exe.northwind.invoicing.prod',
    uxe: 'uxe.northwind.invoicing.prod.worker',
  }
  const matrix = typeCodes.flatMap((ancestor) =>
    typeCodes.map((descendant) => [ancestor, descendant, below[ancestor].includes(descendant)] as const),
  )

  it.each(matrix)('canDescend %s → %s is %s, in any case', (ancestor, descendant, expected) => {
    const upperKey = `${ancestor.toUpperCase()}${sample[ancestor].slice(ancestor.length)}`

    expect(canDescend(sample[ancestor], descendant)).toBe(expected)
    expect(canDescend(upperKey, descendant.toUpperCase())).toBe(expected)
  })

  it.each(typeCodes)('lists the types below a key of type %s in typeCodes order', (ancestor) => {
    expect(descendantTypeCodes(sample[ancestor])).toEqual(below[ancestor])
  })

  it('reads the type code of the key in any case and lists none for an invalid key', () => {
    expect(descendantTypeCodes('RST.invoicing')).toEqual(below.rst)
    for (const key of ['', 'rst', 'rst.', 'rst.a.b', 'exe.northwind.invoicing', 'xyz.a']) {
      expect(descendantTypeCodes(key), key).toEqual([])
    }
  })
})

describe('withRelations', () => {
  it('derives the members the runtime types require from the key', () => {
    expect(withRelations(annotation('exe.northwind.invoicing.prod'))).toMatchObject({
      SubjectKey: 'sbt.northwind',
      SubjectName: 'northwind',
      ResponsibilityKey: 'rst.invoicing',
      ResponsibilityName: 'invoicing',
      ContextKey: 'cnt.northwind.prod',
      ContextName: 'prod',
      UsageKey: 'usg.northwind.invoicing',
    })
    expect(withRelations(annotation('uxe.northwind.invoicing.prod.worker'))).toMatchObject({
      UnitKey: 'unt.invoicing.worker',
      UnitName: 'worker',
      ExecutionKey: 'exe.northwind.invoicing.prod',
    })
    expect(withRelations(annotation('sbt.northwind'))).not.toHaveProperty('SubjectKey')
  })

  it('gives a unit the API defaults and keeps members already present', () => {
    expect(withRelations(annotation('unt.invoicing.worker'))).toMatchObject({
      ResponsibilityKey: 'rst.invoicing',
      UnitType: 'event',
      UnitDefinition: 'unknown',
    })
    expect(withRelations({ ...annotation('unt.invoicing.worker'), UnitType: 'queue' } as Annotation)).toMatchObject({
      UnitType: 'queue',
    })
  })
})

describe('small helpers', () => {
  it('knows type metadata by name, code and key', () => {
    expect(metaOf('Execution')?.code).toBe('exe')
    expect(metaOf('UXE')?.label).toBe('Unit of execution')
    expect(codeOfKey('usg.a.b')).toBe('usg')
    expect(metaOf('nope')).toBeUndefined()
  })

  it('normalizes labels and gives them stable colors', () => {
    expect(normalizeLabels([' eu', 'EU', '', 'pci '])).toEqual(['eu', 'pci'])
    expect(labelColor('eu')).toBe(labelColor('eu'))
  })
})

describe('dates', () => {
  it('formats a date and time in a zone, falling back to the browser zone', async () => {
    const { formatDateTime, formatRelative } = await import('@/utils/dates')
    const date = new Date('2026-10-10T12:00:00Z')

    expect(formatDateTime(date, 'Europe/Prague')).toMatch(/2026/)
    expect(formatDateTime(date, 'Not/AZone')).toMatch(/2026/)
    expect(formatRelative(new Date('2026-10-10T09:00:00Z'), date)).toBe('3 hours ago')
    expect(formatRelative(new Date('2026-10-10T11:59:40Z'), date)).toBe('just now')
  })
})
