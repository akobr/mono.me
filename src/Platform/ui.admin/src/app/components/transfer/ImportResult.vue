<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import type { TableColumn } from '@nuxt/ui'
import type { ViewArchiveContent, ViewImport, ViewImportItem, ViewImportSummary } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'
import {
  archiveContents,
  contentOf,
  documentExists,
  documentKinds,
  documentName,
  documentPath,
  importCounts,
  outcomeLabel,
  outcomes,
  plural,
} from '@/utils/transfers'

/**
 * The result of an import of the view (spec 4.4 of the Export and Import of a View): a banner, the counts per kind
 * and in total, chips that filter the documents by kind and outcome, and the documents that were not unchanged, with
 * their warnings and a link to the document. A table on desktop, a list on phones.
 */
const props = defineProps<{ result: ViewImport; scope: Scope; canImportNow?: boolean; importing?: boolean }>()
const emit = defineEmits<{ importNow: [] }>()

const desktop = useMediaQuery('(min-width: 768px)')
/** Rows rendered at first; the rest come on request, so a result of a thousand documents stays quick. */
const pageSize = 200

type OutcomeFilter = 'Failed' | 'Skipped' | 'Warnings'

const kindFilter = ref(new Set<ViewArchiveContent>())
const outcomeFilter = ref(new Set<OutcomeFilter>())
const limit = ref(pageSize)

watch(
  () => props.result.Id,
  () => {
    kindFilter.value = new Set()
    outcomeFilter.value = new Set()
    limit.value = pageSize
  },
)

const isDryRun = computed(() => !!props.result.IsDryRun)
const failed = computed(() => props.result.Summary?.Failed ?? 0)

const banner = computed(() => ({
  title: isDryRun.value ? 'Checked: nothing was written' : 'Imported',
  description: `${props.result.FileName ?? 'The archive'}: ${importCounts(props.result)}.`,
  color: failed.value > 0 ? ('warning' as const) : isDryRun.value ? ('info' as const) : ('success' as const),
  icon:
    failed.value > 0 ? 'i-lucide-triangle-alert' : isDryRun.value ? 'i-lucide-list-checks' : 'i-lucide-circle-check',
}))

const bannerActions = computed(() =>
  props.canImportNow
    ? [{ label: 'Import now', icon: 'i-lucide-upload', loading: props.importing, onClick: () => emit('importNow') }]
    : undefined,
)

// --- summary ---

const countColumns = computed(
  () =>
    [
      { key: 'Created', label: outcomeLabel('Created', isDryRun.value) },
      { key: 'Updated', label: outcomeLabel('Updated', isDryRun.value) },
      { key: 'Unchanged', label: outcomeLabel('Unchanged', isDryRun.value) },
      { key: 'Skipped', label: outcomeLabel('Skipped', isDryRun.value) },
      { key: 'Failed', label: outcomeLabel('Failed', isDryRun.value) },
      { key: 'Warnings', label: 'Warnings' },
    ] as const,
)

interface SummaryRow {
  label: string
  summary: ViewImportSummary
  total: boolean
}

const summaryRows = computed<SummaryRow[]>(() => [
  ...(props.result.Summaries ?? []).map((summary) => ({ label: summary.Content ?? '', summary, total: false })),
  { label: 'Total', summary: props.result.Summary ?? {}, total: true },
])

/** "2 to create, 5 unchanged, 1 warning" on a phone, and the files of the total that were left out. */
function summaryText(row: SummaryRow): string {
  const { summary } = row
  const parts = [
    summary.Created ? `${summary.Created} ${isDryRun.value ? 'to create' : 'created'}` : '',
    summary.Updated ? `${summary.Updated} ${isDryRun.value ? 'to update' : 'updated'}` : '',
    `${summary.Unchanged ?? 0} unchanged`,
    summary.Skipped ? `${summary.Skipped} ${isDryRun.value ? 'to skip' : 'skipped'}` : '',
    summary.Failed ? `${summary.Failed} failed` : '',
    summary.Warnings ? plural(summary.Warnings, 'warning') : '',
    row.total ? `${summary.ExcludedFiles ?? 0} excluded and ${summary.IgnoredFiles ?? 0} ignored files` : '',
  ]
  return parts.filter(Boolean).join(', ')
}

/** The documents of the included kinds that the archive holds. */
const documentCount = computed(() => {
  const summary = props.result.Summary ?? {}
  return (
    (summary.Created ?? 0) +
    (summary.Updated ?? 0) +
    (summary.Unchanged ?? 0) +
    (summary.Skipped ?? 0) +
    (summary.Failed ?? 0)
  )
})

// --- filters ---

const items = computed(() => props.result.Items ?? [])

const kindChips = computed(() =>
  archiveContents
    .map((content) => ({ content, count: items.value.filter((item) => contentOf(item.Kind) === content).length }))
    .filter((chip) => chip.count > 0),
)

const outcomeChips = computed(() =>
  (
    [
      { filter: 'Failed', label: outcomeLabel('Failed', isDryRun.value), test: (item) => item.Outcome === 'Failed' },
      { filter: 'Skipped', label: outcomeLabel('Skipped', isDryRun.value), test: (item) => item.Outcome === 'Skipped' },
      { filter: 'Warnings', label: 'Warnings', test: (item) => !!item.Warnings?.length },
    ] satisfies { filter: OutcomeFilter; label: string; test: (item: ViewImportItem) => boolean }[]
  )
    .map((chip) => ({ ...chip, count: items.value.filter(chip.test).length }))
    .filter((chip) => chip.count > 0),
)

/** The set without the value if it has it, otherwise with it. */
function toggled<T>(set: ReadonlySet<T>, value: T): Set<T> {
  const next = new Set(set)
  if (!next.delete(value)) {
    next.add(value)
  }
  return next
}

function toggleKind(content: ViewArchiveContent) {
  kindFilter.value = toggled(kindFilter.value, content)
  limit.value = pageSize
}

function toggleOutcome(filter: OutcomeFilter) {
  outcomeFilter.value = toggled(outcomeFilter.value, filter)
  limit.value = pageSize
}

const filtered = computed(() =>
  items.value.filter(
    (item) =>
      (kindFilter.value.size === 0 || kindFilter.value.has(contentOf(item.Kind))) &&
      (outcomeFilter.value.size === 0 ||
        outcomeChips.value.some((chip) => outcomeFilter.value.has(chip.filter) && chip.test(item))),
  ),
)
const shown = computed(() => filtered.value.slice(0, limit.value))
const isFiltered = computed(() => kindFilter.value.size > 0 || outcomeFilter.value.size > 0)

function clearFilters() {
  kindFilter.value = new Set()
  outcomeFilter.value = new Set()
}

// --- documents ---

const linkOf = (item: ViewImportItem) =>
  documentExists(props.result, item) ? documentPath(props.scope, item) : undefined
const kindOf = (item: ViewImportItem) => documentKinds[item.Kind ?? 'Configuration']
const outcomeColor = (item: ViewImportItem) => (item.Outcome ? outcomes[item.Outcome].color : 'neutral')

const columns: TableColumn<ViewImportItem>[] = [
  { id: 'kind', header: 'Kind', meta: { style: { th: 'width: 1%' } } },
  { id: 'document', header: 'Document', meta: { style: { th: 'width: 35%' } } },
  { id: 'outcome', header: 'Outcome', meta: { style: { th: 'width: 1%' } } },
  { id: 'version', header: 'Version', meta: { style: { th: 'width: 1%' } } },
  { id: 'code', header: 'Code', meta: { style: { th: 'width: 1%' } } },
  { id: 'message', header: 'Message', meta: { style: { th: 'width: 35%' } } },
]
</script>

<template>
  <UCard as="section" aria-labelledby="result-heading" :ui="{ body: 'space-y-4' }">
    <template #header>
      <h2 id="result-heading" class="font-semibold text-highlighted">Latest import</h2>
    </template>

    <UAlert
      :title="banner.title"
      :description="banner.description"
      :color="banner.color"
      :icon="banner.icon"
      :actions="bannerActions"
      variant="subtle"
      :orientation="desktop ? 'horizontal' : 'vertical'"
    />

    <!-- the counts per kind, and the total with the files left out -->
    <table v-if="desktop" class="w-full text-sm" aria-label="Summary">
      <thead>
        <tr class="border-b border-default text-left text-xs text-muted">
          <th scope="col" class="py-1.5 pe-3 font-medium">Kind</th>
          <th v-for="column in countColumns" :key="column.key" scope="col" class="px-3 py-1.5 text-end font-medium">
            {{ column.label }}
          </th>
          <th scope="col" class="px-3 py-1.5 text-end font-medium">Excluded files</th>
          <th scope="col" class="ps-3 py-1.5 text-end font-medium">Ignored files</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="row in summaryRows"
          :key="row.label"
          :class="row.total ? 'font-semibold text-highlighted' : 'border-b border-default'"
        >
          <th scope="row" class="py-1.5 pe-3 text-left font-medium">{{ row.label }}</th>
          <td
            v-for="column in countColumns"
            :key="column.key"
            class="px-3 py-1.5 text-end tabular-nums"
            :class="{
              'text-error': column.key === 'Failed' && (row.summary.Failed ?? 0) > 0,
              'text-warning': column.key === 'Warnings' && (row.summary.Warnings ?? 0) > 0,
            }"
          >
            {{ row.summary[column.key] ?? 0 }}
          </td>
          <td class="px-3 py-1.5 text-end tabular-nums">{{ row.total ? (row.summary.ExcludedFiles ?? 0) : '' }}</td>
          <td class="ps-3 py-1.5 text-end tabular-nums">{{ row.total ? (row.summary.IgnoredFiles ?? 0) : '' }}</td>
        </tr>
      </tbody>
    </table>
    <ul v-else class="space-y-1.5 text-sm" aria-label="Summary">
      <li v-for="row in summaryRows" :key="row.label" :class="{ 'font-semibold text-highlighted': row.total }">
        <span class="font-medium">{{ row.label }}:</span> {{ summaryText(row) }}
      </li>
    </ul>

    <template v-if="items.length">
      <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter the documents">
        <UButton
          v-for="chip in kindChips"
          :key="chip.content"
          :label="`${chip.content} ${chip.count}`"
          size="sm"
          :color="kindFilter.has(chip.content) ? 'primary' : 'neutral'"
          :variant="kindFilter.has(chip.content) ? 'soft' : 'outline'"
          :aria-pressed="kindFilter.has(chip.content)"
          @click="toggleKind(chip.content)"
        />
        <span
          v-if="kindChips.length && outcomeChips.length"
          class="mx-1 h-5 w-px bg-(--ui-border)"
          aria-hidden="true"
        />
        <UButton
          v-for="chip in outcomeChips"
          :key="chip.filter"
          :label="`${chip.label} ${chip.count}`"
          size="sm"
          :color="outcomeFilter.has(chip.filter) ? 'primary' : 'neutral'"
          :variant="outcomeFilter.has(chip.filter) ? 'soft' : 'outline'"
          :aria-pressed="outcomeFilter.has(chip.filter)"
          @click="toggleOutcome(chip.filter)"
        />
        <UButton
          v-if="isFiltered"
          label="Clear the filter"
          size="sm"
          color="neutral"
          variant="link"
          @click="clearFilters"
        />
      </div>

      <UTable
        v-if="desktop"
        :data="shown"
        :columns="columns"
        caption="Documents"
        :ui="{ caption: 'sr-only', td: 'py-2 text-sm align-top whitespace-normal', th: 'py-2 text-xs' }"
      >
        <template #kind-cell="{ row }">
          <UBadge
            :icon="kindOf(row.original).icon"
            :label="kindOf(row.original).label"
            color="neutral"
            variant="outline"
            class="whitespace-nowrap"
          />
        </template>
        <template #document-cell="{ row }">
          <div class="min-w-0 space-y-0.5">
            <RouterLink
              v-if="linkOf(row.original)"
              :to="linkOf(row.original)!"
              class="font-mono text-xs break-all text-primary hover:underline"
            >
              {{ documentName(row.original) }}
            </RouterLink>
            <span v-else class="font-mono text-xs break-all text-highlighted">{{ documentName(row.original) }}</span>
            <p class="font-mono text-xs break-all text-muted">{{ row.original.Path }}</p>
          </div>
        </template>
        <template #outcome-cell="{ row }">
          <UBadge
            :color="outcomeColor(row.original)"
            variant="subtle"
            :label="outcomeLabel(row.original.Outcome, isDryRun)"
            class="whitespace-nowrap"
          />
        </template>
        <template #version-cell="{ row }">
          <span v-if="row.original.Version" class="font-mono text-xs">v{{ row.original.Version }}</span>
        </template>
        <template #code-cell="{ row }">
          <code v-if="row.original.ErrorCode" class="text-xs">{{ row.original.ErrorCode }}</code>
        </template>
        <template #message-cell="{ row }">
          <div class="space-y-1 whitespace-normal">
            <p v-if="row.original.Message" class="text-sm break-words">{{ row.original.Message }}</p>
            <ul v-if="row.original.Warnings?.length" class="space-y-1" aria-label="Warnings">
              <li
                v-for="warning in row.original.Warnings"
                :key="`${warning.Code}:${warning.Message}`"
                class="flex items-start gap-1.5 text-xs text-warning"
              >
                <UIcon name="i-lucide-triangle-alert" class="mt-0.5 size-3.5 shrink-0" />
                <span class="break-words"
                  ><code>{{ warning.Code }}</code> {{ warning.Message }}</span
                >
              </li>
            </ul>
          </div>
        </template>
      </UTable>

      <ul v-else class="divide-y divide-default rounded-md border border-default" aria-label="Documents">
        <li v-for="(item, index) in shown" :key="`${item.Path}:${index}`" class="space-y-1.5 px-3 py-2.5 text-sm">
          <div class="flex flex-wrap items-center gap-2">
            <UBadge :icon="kindOf(item).icon" :label="kindOf(item).label" color="neutral" variant="outline" />
            <UBadge :color="outcomeColor(item)" variant="subtle" :label="outcomeLabel(item.Outcome, isDryRun)" />
            <span v-if="item.Version" class="ms-auto font-mono text-xs text-muted">v{{ item.Version }}</span>
          </div>
          <RouterLink
            v-if="linkOf(item)"
            :to="linkOf(item)!"
            class="block font-mono text-xs break-all text-primary hover:underline"
          >
            {{ documentName(item) }}
          </RouterLink>
          <p v-else class="font-mono text-xs break-all text-highlighted">{{ documentName(item) }}</p>
          <p class="font-mono text-xs break-all text-muted">{{ item.Path }}</p>
          <p v-if="item.ErrorCode || item.Message" class="break-words">
            <code v-if="item.ErrorCode" class="me-1 text-xs">{{ item.ErrorCode }}</code>
            <span class="text-muted">{{ item.Message }}</span>
          </p>
          <ul v-if="item.Warnings?.length" class="space-y-1" aria-label="Warnings">
            <li
              v-for="warning in item.Warnings"
              :key="`${warning.Code}:${warning.Message}`"
              class="flex items-start gap-1.5 text-xs text-warning"
            >
              <UIcon name="i-lucide-triangle-alert" class="mt-0.5 size-3.5 shrink-0" />
              <span class="break-words"
                ><code>{{ warning.Code }}</code> {{ warning.Message }}</span
              >
            </li>
          </ul>
        </li>
      </ul>

      <p v-if="filtered.length === 0" class="text-sm text-muted">No document matches the filter.</p>
      <div v-else-if="filtered.length > shown.length" class="flex justify-center">
        <UButton
          :label="`Show all ${filtered.length}`"
          color="neutral"
          variant="outline"
          size="sm"
          @click="limit = filtered.length"
        />
      </div>
    </template>
    <p v-else class="text-sm text-muted">
      {{
        documentCount === 0
          ? 'The archive holds no document of the included kinds.'
          : isDryRun
            ? 'Every document equals the stored one.'
            : 'Every document already equalled the stored one.'
      }}
    </p>

    <p v-if="result.AreItemsTruncated" class="text-xs text-muted">
      Only the first {{ items.length }} documents are listed; the counts include every file.
    </p>
  </UCard>
</template>
