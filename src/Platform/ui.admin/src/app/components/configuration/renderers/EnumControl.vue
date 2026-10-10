<script setup lang="ts">
import { computed } from 'vue'
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// enum (USelect, or USelectMenu with search above 8 options) and oneOf [{ const, title }] (the titles are shown,
// the const is stored).
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const schema = computed(() => control.value.schema as Record<string, unknown>)
const items = computed(() => {
  if (Array.isArray(schema.value.oneOf)) {
    return (schema.value.oneOf as { const?: unknown; title?: string }[]).map((entry) => ({
      label: entry.title ?? String(entry.const),
      value: entry.const as string | number,
    }))
  }
  return ((schema.value.enum as unknown[]) ?? []).map((value) => ({
    label: String(value),
    value: value as string | number,
  }))
})
const searchable = computed(() => items.value.length > 8)
const value = computed(() => control.value.data as string | number | undefined)
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ id, readonly, placeholder }">
      <USelectMenu
        v-if="searchable"
        :id="id"
        :model-value="value"
        :items="items"
        value-key="value"
        :disabled="readonly"
        :placeholder="placeholder ?? 'Select…'"
        class="w-full max-w-sm"
        @update:model-value="(value) => handleChange(control.path, value)"
      />
      <USelect
        v-else
        :id="id"
        :model-value="value"
        :items="items"
        :disabled="readonly"
        :placeholder="placeholder ?? 'Select…'"
        class="w-full max-w-sm"
        @update:model-value="(value) => handleChange(control.path, value)"
      />
    </template>
  </ControlField>
</template>
