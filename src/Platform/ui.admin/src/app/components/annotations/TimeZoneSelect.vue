<script setup lang="ts">
import { computed } from 'vue'

/** Searchable IANA time zones with their current offset (spec section 11.1). */
const model = defineModel<string>({ default: '' })
withDefaults(defineProps<{ disabled?: boolean }>(), { disabled: false })

const offset = (timeZone: string) => {
  try {
    const part = new Intl.DateTimeFormat('en', { timeZone, timeZoneName: 'shortOffset' })
      .formatToParts(new Date())
      .find((entry) => entry.type === 'timeZoneName')
    return part?.value ?? ''
  } catch {
    return ''
  }
}

const zones = computed(() => {
  const names = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []
  return ['UTC', ...names.filter((name) => name !== 'UTC')].map((name) => ({
    label: name,
    value: name,
    suffix: offset(name),
  }))
})

const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone
</script>

<template>
  <USelectMenu
    v-model="model"
    :items="zones"
    value-key="value"
    :disabled="disabled"
    :placeholder="`Browser time zone (${browserZone})`"
    :search-input="{ placeholder: 'Search time zones…' }"
    virtualize
    class="w-full"
    aria-label="Time zone"
  >
    <template #item-trailing="{ item }">
      <span class="text-xs text-dimmed">{{ (item as { suffix: string }).suffix }}</span>
    </template>
  </USelectMenu>
</template>
