<script setup lang="ts">
import { computed } from 'vue'
import { isCatalogTimeZone } from '@42for.net/2splatform.sdk'

/**
 * Searchable IANA time zones with their current offset (spec section 11.1): the zones the browser knows that the API
 * accepts too (the time zones of the catalog of project settings, 424-266), so no zone is offered that a save refuses.
 */
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
  return ['UTC', ...names.filter((name) => name !== 'UTC' && isCatalogTimeZone(name))].map((name) => ({
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
