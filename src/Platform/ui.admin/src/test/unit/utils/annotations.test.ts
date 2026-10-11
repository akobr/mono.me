import { describe, expect, it } from 'vitest'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { annotationStatus } from '@/utils/annotation-status'
import { expandableKeys, projectTree, type TreeNode } from '@/utils/tree-projection'
import { codeOfKey, deletedWith, metaOf } from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { labelColor, normalizeLabels } from '@/utils/labels'
import { renderInlineMarkdown } from '@/utils/markdown'

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

  it('projects the DAG by subject, with missing parents as placeholders', () => {
    expect(shape(projectTree(data, 'subject'))).toEqual([
      {
        '(missing) sbt.contoso': [
          { '(missing) cnt.contoso.eu': [{ '(missing) exe.contoso.invoicing.eu': ['uxe.contoso.invoicing.eu.pdf'] }] },
        ],
      },
      {
        'sbt.northwind': [
          { 'cnt.northwind.prod': [{ 'exe.northwind.invoicing.prod': ['uxe.northwind.invoicing.prod.pdf'] }] },
          'usg.northwind.invoicing',
        ],
      },
      { 'rst.invoicing': ['unt.invoicing.pdf'] },
    ])
  })

  it('projects by responsibility', () => {
    const roots = projectTree(
      data.filter((entry) => !entry.AnnotationKey.includes('contoso')),
      'responsibility',
    )

    expect(shape(roots)).toEqual([
      {
        'rst.invoicing': [
          'unt.invoicing.pdf',
          { 'usg.northwind.invoicing': [{ 'exe.northwind.invoicing.prod': ['uxe.northwind.invoicing.prod.pdf'] }] },
        ],
      },
      { 'sbt.northwind': ['cnt.northwind.prod'] },
    ])
  })

  it('keeps the ancestor chain of matches and marks it as not matched', () => {
    const roots = projectTree(data, 'subject', (entry) => entry.AnnotationKey === 'uxe.northwind.invoicing.prod.pdf')
    const flat: TreeNode[] = []
    const walk = (node: TreeNode) => (flat.push(node), node.children.forEach(walk))
    roots.forEach(walk)

    expect(flat.map((node) => [node.key, node.matched, node.depth])).toEqual([
      ['sbt.northwind', false, 0],
      ['cnt.northwind.prod', false, 1],
      ['exe.northwind.invoicing.prod', false, 2],
      ['uxe.northwind.invoicing.prod.pdf', true, 3],
    ])
    expect(expandableKeys(roots)).toEqual(['sbt.northwind', 'cnt.northwind.prod', 'exe.northwind.invoicing.prod'])
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

  it('follows the cascade of DeleteAnnotationAsync per type', () => {
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
      ['cnt.northwind.prod', 'usg.northwind.invoicing', 'exe.northwind.invoicing.prod', 'uxe.northwind.invoicing.prod.worker'].toSorted(),
    )
    expect(deletedWith('usg.northwind.invoicing', keys).toSorted()).toEqual(
      ['exe.northwind.invoicing.prod', 'uxe.northwind.invoicing.prod.worker'].toSorted(),
    )
    expect(deletedWith('unt.invoicing.worker', keys)).toEqual(['uxe.northwind.invoicing.prod.worker'])
    // A context takes its executions, not their units of execution; an execution goes alone.
    expect(deletedWith('cnt.northwind.prod', keys)).toEqual(['exe.northwind.invoicing.prod'])
    expect(deletedWith('exe.northwind.invoicing.prod', keys)).toEqual([])
    expect(deletedWith('uxe.northwind.invoicing.prod.worker', keys)).toEqual([])
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

  it('renders a safe markdown subset', () => {
    expect(
      renderInlineMarkdown('**bold** `code` <script>x</script> [docs](https://d.test) [bad](javascript:alert(1))'),
    ).toBe(
      '<p><strong>bold</strong> <code>code</code> &lt;script&gt;x&lt;/script&gt; <a href="https://d.test" target="_blank" rel="noopener noreferrer" class="text-primary underline">docs</a> [bad](javascript:alert(1))</p>',
    )
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
