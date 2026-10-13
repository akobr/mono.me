<script setup lang="ts">
import { computed } from 'vue'
import {
  CalendarDateTime,
  parseAbsolute,
  Time,
  toCalendarDate,
  toTime,
  toZoned,
  type CalendarDate,
} from '@internationalized/date'
import { isValidTimeZone } from '@/forms/annotation.schema'
import { dateInputLocale, formatDateTime } from '@/utils/dates'

/**
 * A date and a time interpreted in the annotation's time zone (spec section 11.1). The model is an ISO instant
 * (UTC); the tooltip shows the same instant in the user's local zone. The date segments follow the date format in
 * effect (424-23).
 */
const model = defineModel<string | null | undefined>()
const props = withDefaults(defineProps<{ timeZone?: string; disabled?: boolean; label: string }>(), {
  timeZone: undefined,
  disabled: false,
})

const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone
const zone = computed(() => (props.timeZone && isValidTimeZone(props.timeZone) ? props.timeZone : browserZone))

const zoned = computed(() => {
  if (!model.value) {
    return undefined
  }
  try {
    return parseAbsolute(model.value, zone.value)
  } catch {
    return undefined
  }
})

const date = computed(() => (zoned.value ? toCalendarDate(zoned.value) : undefined))
const time = computed(() => (zoned.value ? toTime(zoned.value) : undefined))
const local = computed(() => (model.value ? `Your time: ${formatDateTime(new Date(model.value))}` : ''))
const locale = computed(() => dateInputLocale())

function update(nextDate: CalendarDate | undefined, nextTime: Time | undefined) {
  if (!nextDate) {
    model.value = null
    return
  }
  const timeValue = nextTime ?? new Time(0, 0)
  const value = new CalendarDateTime(nextDate.year, nextDate.month, nextDate.day, timeValue.hour, timeValue.minute)
  model.value = toZoned(value, zone.value).toAbsoluteString()
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <UInputDate
      :model-value="date"
      :locale="locale"
      :disabled="disabled"
      :aria-label="`${label} date`"
      @update:model-value="(value) => update((value as CalendarDate | undefined) ?? undefined, time)"
    />
    <UInputTime
      :model-value="time"
      :disabled="disabled || !date"
      :aria-label="`${label} time`"
      :hour-cycle="24"
      @update:model-value="(value) => update(date, (value as Time | undefined) ?? undefined)"
    />
    <UTooltip v-if="model" :text="local">
      <span class="text-xs text-muted">{{ zone }}</span>
    </UTooltip>
    <UButton v-if="model && !disabled" label="Clear" color="neutral" variant="ghost" size="xs" @click="model = null" />
  </div>
</template>
