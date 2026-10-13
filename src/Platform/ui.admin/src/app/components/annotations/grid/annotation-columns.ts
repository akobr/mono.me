import { h } from 'vue'
import type { TableColumn } from '@nuxt/ui'
import type { HeaderContext } from '@tanstack/vue-table'
import type { ConfigurationSummary } from '@42for.net/2splatform.sdk'
import { metaOf } from '@/utils/annotation-key'
import { annotationStatus, annotationStatuses } from '@/utils/annotation-status'
import { formatDateTime } from '@/utils/dates'
import type { TreeNode } from '@/utils/tree-projection'
import AnnotationStatusBadge from '../AnnotationStatusBadge.vue'
import AnnotationTypeBadge from '../AnnotationTypeBadge.vue'
import KeyText from '../../common/KeyText.vue'
import ColumnHeader from './ColumnHeader.vue'
import LabelChips from './LabelChips.vue'
import RowSelect from './RowSelect.vue'
import TreeCell from './TreeCell.vue'

export interface ColumnOptions {
  tree: boolean
  now: number
  configurations: ReadonlyMap<string, ConfigurationSummary> | null
  /** By row id: in the tree, an annotation can have a row per path. */
  isExpanded: (id: string) => boolean
  toggle: (id: string) => void
}

export const columnLabels: Record<string, string> = {
  type: 'Type',
  name: 'Name',
  key: 'Key',
  title: 'Title',
  labels: 'Labels',
  status: 'Status',
  configuration: 'Configuration',
  validFrom: 'Valid from',
  expiresAt: 'Expires',
}

/** Columns hidden until the user shows them (spec section 8). */
export const defaultHiddenColumns = ['validFrom', 'expiresAt']

const width = (size: number, min = 60) => ({
  th: (header: { column: { getSize: () => number } }) => ({
    width: `${header.column.getSize() || size}px`,
    minWidth: `${min}px`,
  }),
  td: (cell: { column: { getSize: () => number } }) => ({
    width: `${cell.column.getSize() || size}px`,
    maxWidth: `${cell.column.getSize() || size}px`,
  }),
})

const header = (id: string) => (context: HeaderContext<TreeNode, unknown>) =>
  h(ColumnHeader, { header: context.header, label: columnLabels[id]! })

const dateCell = (value: string | null | undefined, timeZone?: string) =>
  value
    ? h('span', { class: 'text-xs whitespace-nowrap' }, formatDateTime(new Date(value), timeZone))
    : h('span', { class: 'text-dimmed' }, '–')

export function annotationColumns(options: ColumnOptions): TableColumn<TreeNode>[] {
  return [
    {
      id: 'select',
      size: 40,
      enableSorting: false,
      enableHiding: false,
      enableResizing: false,
      header: ({ table }) =>
        h(RowSelect, {
          checked: table.getIsAllRowsSelected() ? true : table.getIsSomeRowsSelected() ? 'indeterminate' : false,
          label: 'Select all rows',
          'onUpdate:checked': (value: boolean) => table.toggleAllRowsSelected(value),
        }),
      cell: ({ row }) =>
        h(RowSelect, {
          checked: row.getIsSelected(),
          disabled: !row.getCanSelect(),
          label: `Select ${row.original.key}`,
          'onUpdate:checked': (value: boolean) => row.toggleSelected(value),
        }),
      meta: { style: width(40, 40) },
    },
    {
      id: 'type',
      size: options.tree ? 260 : 160,
      accessorFn: (row) => metaOf(row.key.slice(0, 3))?.mergeOrder ?? 99,
      header: header('type'),
      cell: ({ row }) =>
        options.tree
          ? h(TreeCell, {
              node: row.original,
              expanded: options.isExpanded(row.original.id),
              onToggle: () => options.toggle(row.original.id),
            })
          : h(AnnotationTypeBadge, { type: row.original.key.slice(0, 3), variant: 'label' }),
      meta: { style: width(options.tree ? 260 : 160) },
    },
    {
      id: 'name',
      size: 180,
      accessorFn: (row) => row.annotation?.Name ?? row.key.split('.').at(-1),
      header: header('name'),
      cell: ({ row }) =>
        row.original.missing
          ? h('span', { class: 'italic text-dimmed line-through' }, `(missing) ${row.original.key.split('.').at(-1)}`)
          : h('span', { class: 'font-medium text-highlighted truncate block' }, row.original.annotation?.Name),
      meta: { style: width(180) },
    },
    {
      id: 'key',
      size: 300,
      accessorFn: (row) => row.key,
      header: header('key'),
      cell: ({ row }) => h(KeyText, { value: row.original.key }),
      meta: { style: width(300) },
    },
    {
      id: 'title',
      size: 260,
      accessorFn: (row) => row.annotation?.Title ?? '',
      header: header('title'),
      cell: ({ row }) =>
        h(
          'span',
          { class: 'block truncate text-muted', title: row.original.annotation?.Title },
          row.original.annotation?.Title ?? '',
        ),
      meta: { style: width(260) },
    },
    {
      id: 'labels',
      size: 200,
      enableSorting: false,
      header: header('labels'),
      cell: ({ row }) => h(LabelChips, { labels: row.original.annotation?.Labels ?? [] }),
      meta: { style: width(200) },
    },
    {
      id: 'status',
      size: 120,
      accessorFn: (row) =>
        row.annotation ? annotationStatuses.indexOf(annotationStatus(row.annotation, options.now)) : 99,
      header: header('status'),
      cell: ({ row }) =>
        row.original.annotation
          ? h(AnnotationStatusBadge, { status: annotationStatus(row.original.annotation, options.now) })
          : null,
      meta: { style: width(120) },
    },
    {
      id: 'configuration',
      size: 120,
      accessorFn: (row) => options.configurations?.get(row.key)?.Version ?? -1,
      header: header('configuration'),
      cell: ({ row }) => {
        const summary = options.configurations?.get(row.original.key)
        return summary
          ? h('span', { class: 'font-mono text-xs rounded bg-elevated px-1.5 py-0.5' }, `v${summary.Version}`)
          : h('span', { class: 'text-dimmed' }, options.configurations ? '–' : '?')
      },
      meta: { style: width(120) },
    },
    {
      id: 'validFrom',
      size: 170,
      accessorFn: (row) => row.annotation?.ValidFrom ?? '',
      header: header('validFrom'),
      cell: ({ row }) => dateCell(row.original.annotation?.ValidFrom, row.original.annotation?.TimeZone),
      meta: { style: width(170) },
    },
    {
      id: 'expiresAt',
      size: 170,
      accessorFn: (row) => row.annotation?.ExpiresAt ?? '',
      header: header('expiresAt'),
      cell: ({ row }) => dateCell(row.original.annotation?.ExpiresAt, row.original.annotation?.TimeZone),
      meta: { style: width(170) },
    },
  ]
}
