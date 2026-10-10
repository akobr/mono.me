<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'

type ValueType = 'string' | 'number' | 'boolean' | 'null' | 'object' | 'array'

interface Row {
  id: number
  key: string
  type: ValueType
  value: unknown
}

/**
 * Annotation Values (spec section 11.1): one row per key with its type and value. Primitives are edited inline,
 * objects and arrays in a popover editor; "Edit all as JSON/YAML" edits the whole map.
 */
const model = defineModel<Record<string, unknown>>({ default: () => ({}) })
withDefaults(defineProps<{ disabled?: boolean }>(), { disabled: false })

const preferences = usePreferencesStore()
const types: ValueType[] = ['string', 'number', 'boolean', 'null', 'object', 'array']
const defaults: Record<ValueType, () => unknown> = {
  string: () => '',
  number: () => 0,
  boolean: () => false,
  null: () => null,
  object: () => ({}),
  array: () => [],
}

let nextId = 0
const typeOf = (value: unknown): ValueType =>
  value === null
    ? 'null'
    : Array.isArray(value)
      ? 'array'
      : typeof value === 'object'
        ? 'object'
        : (typeof value as ValueType)

const toRows = (record: Record<string, unknown>): Row[] =>
  Object.entries(record ?? {}).map(([key, value]) => ({ id: nextId++, key, type: typeOf(value), value }))

const rows = ref<Row[]>(toRows(model.value))
const asDocument = ref(false)
const documentFormat = ref<DocumentFormat>(preferences.state.editorFormat)
let lastEmitted = JSON.stringify(model.value ?? {})

const duplicates = computed(() => {
  const seen = new Set<string>()
  const result = new Set<string>()
  for (const row of rows.value) {
    const key = row.key.trim()
    if (seen.has(key)) {
      result.add(key)
    }
    seen.add(key)
  }
  return result
})

function emit() {
  const record = Object.fromEntries(rows.value.map((row) => [row.key.trim(), row.value]))
  lastEmitted = JSON.stringify(record)
  model.value = record
}

watch(
  model,
  (value) => {
    if (JSON.stringify(value ?? {}) !== lastEmitted) {
      lastEmitted = JSON.stringify(value ?? {})
      rows.value = toRows(value)
    }
  },
  { deep: true },
)

function setType(row: Row, type: ValueType) {
  row.type = type
  row.value = defaults[type]()
  emit()
}

function add() {
  let index = rows.value.length + 1
  while (rows.value.some((row) => row.key === `key${index}`)) {
    index++
  }
  rows.value.push({ id: nextId++, key: `key${index}`, type: 'string', value: '' })
  emit()
}

function remove(row: Row) {
  rows.value = rows.value.filter((entry) => entry !== row)
  emit()
}

const preview = (value: unknown) => {
  const text = JSON.stringify(value)
  return text.length > 40 ? `${text.slice(0, 40)}…` : text
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex items-center justify-between gap-2">
      <USwitch v-model="asDocument" size="sm" label="Edit all as JSON/YAML" :disabled="disabled" />
      <UButton
        v-if="!asDocument && !disabled"
        label="Add value"
        icon="i-lucide-plus"
        size="xs"
        color="neutral"
        variant="outline"
        @click="add"
      />
    </div>

    <DocumentEditor
      v-if="asDocument"
      :model-value="model"
      :format="documentFormat"
      :readonly="disabled"
      :validate="
        (value) =>
          value && typeof value === 'object' && !Array.isArray(value) ? [] : [{ message: 'Values must be an object.' }]
      "
      aria-label="Values"
      @update:format="(format) => (documentFormat = format)"
      @update:model-value="
        (value) =>
          value && typeof value === 'object' && !Array.isArray(value) && (model = value as Record<string, unknown>)
      "
    />

    <template v-else>
      <p v-if="rows.length === 0" class="text-sm text-muted">No values.</p>
      <div
        v-for="row in rows"
        :key="row.id"
        class="grid grid-cols-[minmax(0,1fr)_7rem_minmax(0,1.4fr)_auto] items-center gap-2"
      >
        <UInput
          v-model="row.key"
          size="sm"
          class="font-mono"
          aria-label="Value key"
          :disabled="disabled"
          :color="duplicates.has(row.key.trim()) || !row.key.trim() ? 'error' : 'primary'"
          :highlight="duplicates.has(row.key.trim()) || !row.key.trim()"
          @update:model-value="emit"
        />
        <USelect
          :model-value="row.type"
          :items="types"
          size="sm"
          aria-label="Value type"
          :disabled="disabled"
          @update:model-value="(type) => setType(row, type as ValueType)"
        />
        <UInput
          v-if="row.type === 'string'"
          v-model="row.value as string"
          size="sm"
          aria-label="Value"
          :disabled="disabled"
          @update:model-value="emit"
        />
        <UInputNumber
          v-else-if="row.type === 'number'"
          v-model="row.value as number"
          size="sm"
          aria-label="Value"
          :disabled="disabled"
          @update:model-value="emit"
        />
        <USwitch
          v-else-if="row.type === 'boolean'"
          v-model="row.value as boolean"
          aria-label="Value"
          :disabled="disabled"
          @update:model-value="emit"
        />
        <span v-else-if="row.type === 'null'" class="font-mono text-sm text-dimmed">null</span>
        <UPopover v-else :content="{ align: 'start' }">
          <UButton
            :label="preview(row.value)"
            size="sm"
            color="neutral"
            variant="outline"
            class="min-w-0 justify-start font-mono"
            :ui="{ label: 'truncate' }"
          />
          <template #content>
            <div class="w-[min(32rem,90vw)] p-2">
              <DocumentEditor
                :model-value="row.value"
                :format="documentFormat"
                :readonly="disabled"
                :height="240"
                :aria-label="`Value of ${row.key}`"
                @update:format="(format) => (documentFormat = format)"
                @update:model-value="(value) => ((row.value = value), (row.type = typeOf(value)), emit())"
              />
            </div>
          </template>
        </UPopover>
        <UButton
          v-if="!disabled"
          icon="i-lucide-trash-2"
          size="xs"
          color="neutral"
          variant="ghost"
          :aria-label="`Remove ${row.key}`"
          @click="remove(row)"
        />
      </div>
      <p v-if="duplicates.size" class="text-xs text-error">Duplicate keys: {{ [...duplicates].join(', ') }}</p>
    </template>
  </div>
</template>
