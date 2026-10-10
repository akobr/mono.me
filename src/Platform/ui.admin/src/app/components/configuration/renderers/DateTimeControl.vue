<script setup lang="ts">
import { computed } from 'vue'
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// format date, time and date-time. Values stay strings in the JSON Schema formats (RFC 3339); a date-time is
// edited in local time and stored as an ISO instant.
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const format = computed(() => (control.value.schema as { format?: string }).format)
const type = computed(() => (format.value === 'date' ? 'date' : format.value === 'time' ? 'time' : 'datetime-local'))

const display = computed(() => {
  const value = control.value.data as string | undefined
  if (!value || type.value !== 'datetime-local') {
    return value ?? ''
  }
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
})

function update(value: string | number | undefined) {
  const text = value === undefined ? '' : String(value)
  if (!text) {
    handleChange(control.value.path, undefined)
  } else if (type.value === 'datetime-local') {
    handleChange(control.value.path, new Date(text).toISOString())
  } else if (type.value === 'time' && text.length === 5) {
    handleChange(control.value.path, `${text}:00`)
  } else {
    handleChange(control.value.path, text)
  }
}
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ id, readonly, placeholder }">
      <UInput
        :id="id"
        :model-value="display"
        :type="type"
        :disabled="readonly"
        :placeholder="placeholder"
        class="w-full max-w-64"
        @update:model-value="update"
      />
    </template>
  </ControlField>
</template>
