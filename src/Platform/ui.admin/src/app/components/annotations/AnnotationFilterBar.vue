<script setup lang="ts">
import { computed, ref, useTemplateRef, watch } from 'vue'
import { watchDebounced } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'
import { removeTerm, type ParsedFilter } from '@/utils/filter-parser'
import type { ListMode } from '@/composables/useAnnotationFilter'
import type { TreePerspective } from '@/utils/tree-projection'

/**
 * Filter box with chips for the parsed qualifiers, the Grid | Tree switch and the list tools (spec section 8).
 * Compact (below md): the box and a button for the filter bottom sheet.
 */
const props = defineProps<{
  query: string
  parsed: ParsedFilter
  mode: ListMode
  perspective: TreePerspective
  shown: number
  total: number
  columns?: DropdownMenuItem[]
  compact?: boolean
}>()

const emit = defineEmits<{
  'update:query': [value: string]
  'update:mode': [value: ListMode]
  'update:perspective': [value: TreePerspective]
  expandAll: []
  collapseAll: []
}>()

const input = useTemplateRef<{ inputRef: HTMLInputElement }>('input')
const text = ref(props.query)

watch(
  () => props.query,
  (value) => {
    if (value !== text.value.trim()) {
      text.value = value
    }
  },
)
watchDebounced(text, (value) => emit('update:query', value), { debounce: 200 })

const formatter = new Intl.NumberFormat()
const counts = computed(() =>
  props.parsed.terms.length > 0
    ? `${formatter.format(props.shown)} of ${formatter.format(props.total)}`
    : formatter.format(props.total),
)

const modes = [
  { label: 'Grid', value: 'grid', icon: 'i-lucide-table' },
  { label: 'Tree', value: 'tree', icon: 'i-lucide-list-tree' },
]
const perspectives = [
  { label: 'By subject', value: 'subject' },
  { label: 'By responsibility', value: 'responsibility' },
]

const examples = [
  ['type:exe label:eu,us -is:disabled', 'executions labelled eu or us that are not disabled'],
  ['subject:northwind has:config', 'annotations of the northwind subject with a configuration'],
  ['ancestor:rst.invoicing "late fee"', 'below rst.invoicing, containing "late fee"'],
  ['key:exe.*.payments.*', 'keys matching a pattern'],
] as const

function focus() {
  input.value?.inputRef?.focus()
}

defineShortcuts({ '/': focus })
defineExpose({ focus })
</script>

<template>
  <div class="flex w-full flex-col gap-2">
    <div class="flex flex-wrap items-center gap-2">
      <UInput
        ref="input"
        v-model="text"
        icon="i-lucide-search"
        placeholder="Filter: type:exe label:eu is:active …"
        class="min-w-0 flex-1 basis-64"
        :ui="{ base: 'font-mono text-sm' }"
        aria-label="Filter annotations"
      >
        <template #trailing>
          <UButton
            v-if="text"
            icon="i-lucide-x"
            color="neutral"
            variant="link"
            size="xs"
            aria-label="Clear the filter"
            @click="text = ''"
          />
          <UPopover :content="{ align: 'end' }">
            <UButton icon="i-lucide-circle-help" color="neutral" variant="link" size="xs" aria-label="Filter syntax" />
            <template #content>
              <div class="max-w-sm space-y-2 p-3 text-xs">
                <p class="font-semibold text-highlighted">Filter syntax</p>
                <p class="text-muted">
                  Qualifiers: <code>type</code>, <code>label</code>, <code>is</code>, <code>has</code>,
                  <code>key</code>, <code>subject</code>, <code>responsibility</code>, <code>context</code>,
                  <code>unit</code>, <code>ancestor</code>. A comma means OR, <code>-</code> negates, quotes keep
                  spaces; other words search name, key, title and description.
                </p>
                <ul class="space-y-1">
                  <li v-for="[example, description] in examples" :key="example">
                    <button type="button" class="font-mono text-primary hover:underline" @click="text = example">
                      {{ example }}
                    </button>
                    <span class="block text-muted">{{ description }}</span>
                  </li>
                </ul>
              </div>
            </template>
          </UPopover>
        </template>
      </UInput>

      <AnnotationFilterSheet v-if="compact" :query="text" @update:query="(value) => (text = value)" />
      <UTabs
        v-if="!compact"
        :model-value="mode"
        :items="modes"
        :content="false"
        size="xs"
        aria-label="List mode"
        @update:model-value="(value) => emit('update:mode', value as ListMode)"
      />
      <template v-if="mode === 'tree' && !compact">
        <USelect
          :model-value="perspective"
          :items="perspectives"
          size="sm"
          class="w-44"
          aria-label="Tree perspective"
          @update:model-value="(value) => emit('update:perspective', value as TreePerspective)"
        />
        <UTooltip text="Expand all">
          <UButton
            icon="i-lucide-chevrons-up-down"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Expand all"
            @click="emit('expandAll')"
          />
        </UTooltip>
        <UTooltip text="Collapse all">
          <UButton
            icon="i-lucide-chevrons-down-up"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Collapse all"
            @click="emit('collapseAll')"
          />
        </UTooltip>
      </template>
      <UDropdownMenu v-if="columns?.length && !compact" :items="columns" :content="{ align: 'end' }">
        <UButton icon="i-lucide-columns-3" color="neutral" variant="ghost" size="sm" aria-label="Columns" />
      </UDropdownMenu>
      <span class="ms-auto text-xs whitespace-nowrap text-muted" aria-live="polite">{{ counts }}</span>
    </div>

    <div v-if="parsed.terms.length" class="flex flex-wrap items-center gap-1.5">
      <UBadge
        v-for="(term, index) in parsed.terms"
        :key="`${index}:${term.raw}`"
        :color="term.error ? 'warning' : term.negated ? 'error' : 'primary'"
        variant="subtle"
        size="sm"
        class="font-mono"
        :title="term.error"
      >
        {{ term.negated ? 'not ' : '' }}{{ term.qualifier ? `${term.qualifier}: ` : '' }}{{ term.values.join(' or ') }}
        <button
          type="button"
          class="-me-0.5 ms-0.5 rounded hover:bg-black/10"
          :aria-label="`Remove ${term.raw}`"
          @click="text = removeTerm(parsed, index)"
        >
          <UIcon name="i-lucide-x" class="size-3" />
        </button>
      </UBadge>
      <span v-if="parsed.errors.length" class="text-xs text-warning">{{ parsed.errors[0] }}</span>
    </div>
  </div>
</template>
