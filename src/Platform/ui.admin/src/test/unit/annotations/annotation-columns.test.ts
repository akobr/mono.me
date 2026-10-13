import { describe, expect, it } from 'vitest'
import {
  annotationColumns,
  columnLabels,
  defaultHiddenColumns,
  type ColumnOptions,
} from '@/components/annotations/grid/annotation-columns'

const options = (tree: boolean): ColumnOptions => ({
  tree,
  now: Date.parse('2026-10-13T12:00:00Z'),
  configurations: null,
  isExpanded: () => false,
  toggle: () => {},
})

const columnIds = (tree: boolean) => annotationColumns(options(tree)).map((column) => column.id)

describe('annotation grid columns', () => {
  it.each([false, true])('has no description or documentation column (tree: %s)', (tree) => {
    expect(columnIds(tree)).not.toContain('description')
    expect(columnIds(tree)).not.toContain('documentation')
  })

  it('does not offer the description or documentation column in the Columns menu', () => {
    expect(Object.keys(columnLabels)).not.toContain('description')
    expect(Object.keys(columnLabels)).not.toContain('documentation')
    expect(defaultHiddenColumns).toEqual(['validFrom', 'expiresAt'])
  })

  it('keeps the labels and the hidden defaults in step with the columns', () => {
    const columns = annotationColumns(options(false))
    const hideable = columns.filter((column) => column.enableHiding !== false).map((column) => column.id)

    expect(hideable.toSorted()).toEqual(Object.keys(columnLabels).toSorted())
    expect(hideable).toEqual(expect.arrayContaining(defaultHiddenColumns))
  })
})
