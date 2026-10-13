<script setup lang="ts">
import { computed } from 'vue'
import { parseAbsolute, parseDate, type CalendarDate } from '@internationalized/date'
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'
import { dateInputLocale, formatDateParts, formatDateTime } from '@/utils/dates'

// format date, time and date-time. Values stay strings in the JSON Schema formats (RFC 3339). A date and a date-time
// show their segments in the date format in effect (424-23); a date-time is edited in the browser's zone and stored as
// an ISO instant. A value the fields cannot read keeps a text input, so nothing is hidden.
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const format = computed(() => (control.value.schema as { format?: string }).format)
const value = computed(() => (typeof control.value.data === 'string' ? control.value.data : undefined))

function toCalendarDate(text: string | undefined): CalendarDate | undefined {
  try {
    return text ? parseDate(text) : undefined
  } catch {
    return undefined
  }
}

function isInstant(text: string | undefined): boolean {
  try {
    return !!text && !!parseAbsolute(text, 'UTC')
  } catch {
    return false
  }
}

const kind = computed(() => {
  if (format.value === 'date') {
    return !value.value || toCalendarDate(value.value) ? 'date' : 'text'
  }
  if (format.value === 'date-time') {
    return !value.value || isInstant(value.value) ? 'date-time' : 'text'
  }
  return format.value === 'time' ? 'time' : 'text'
})

const date = computed(() => toCalendarDate(value.value))
const locale = computed(() => dateInputLocale())

/** The date fields have no text placeholder, so the inherited value is shown next to them, in the date format. */
function inherited(placeholder: string | undefined): string | undefined {
  const calendar = format.value === 'date' ? toCalendarDate(placeholder) : undefined
  if (calendar) {
    return formatDateParts(calendar.year, calendar.month, calendar.day)
  }
  return format.value === 'date-time' && isInstant(placeholder) ? formatDateTime(new Date(placeholder!)) : placeholder
}

function update(next: string | number | null | undefined) {
  const text = next === undefined || next === null ? '' : String(next)
  if (!text) {
    handleChange(control.value.path, undefined)
  } else if (kind.value === 'time' && text.length === 5) {
    handleChange(control.value.path, `${text}:00`)
  } else {
    handleChange(control.value.path, text)
  }
}
</script>

<template>
  <ControlField :control="control" @change="(next) => handleChange(control.path, next)">
    <template #default="{ id, readonly, placeholder }">
      <div v-if="kind === 'date' || kind === 'date-time'" class="flex flex-wrap items-center gap-2">
        <template v-if="kind === 'date'">
          <UInputDate
            :id="id"
            :model-value="date"
            :locale="locale"
            :disabled="readonly"
            :aria-label="`${control.label} date`"
            @update:model-value="(next) => update((next as CalendarDate | undefined)?.toString())"
          />
          <UButton
            v-if="value && !readonly"
            label="Clear"
            color="neutral"
            variant="ghost"
            size="xs"
            @click="update(undefined)"
          />
        </template>
        <ZonedDateTimeInput
          v-else
          :model-value="value"
          :label="control.label"
          :disabled="readonly"
          @update:model-value="update"
        />
        <span v-if="placeholder" class="text-xs text-muted">Inherited: {{ inherited(placeholder) }}</span>
      </div>
      <UInput
        v-else
        :id="id"
        :model-value="value ?? ''"
        :type="kind"
        :disabled="readonly"
        :placeholder="placeholder"
        class="w-full max-w-64"
        @update:model-value="update"
      />
    </template>
  </ControlField>
</template>
