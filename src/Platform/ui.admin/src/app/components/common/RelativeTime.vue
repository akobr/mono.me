<script setup lang="ts">
import { computed } from 'vue'
import { useNow } from '@vueuse/core'
import { formatDateTime, formatRelative } from '@/utils/dates'

const props = defineProps<{ value?: string | null; timeZone?: string | null }>()

const now = useNow({ interval: 60_000 })
const date = computed(() => (props.value ? new Date(props.value) : undefined))
const relative = computed(() => (date.value ? formatRelative(date.value, now.value) : '–'))
const absolute = computed(() => (date.value ? formatDateTime(date.value, props.timeZone ?? undefined) : ''))
</script>

<template>
  <UTooltip v-if="date" :text="absolute">
    <time :datetime="value ?? undefined" class="whitespace-nowrap">{{ relative }}</time>
  </UTooltip>
  <span v-else class="text-dimmed">–</span>
</template>
