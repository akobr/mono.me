<script setup lang="ts">
import { computed } from 'vue'
import { describeError } from '@/utils/errors'

const props = defineProps<{ error: unknown; title?: string; compact?: boolean }>()
defineEmits<{ retry: [] }>()

const description = computed(() => describeError(props.error, props.title))
const icon = computed(() =>
  description.value.status === 403
    ? 'i-lucide-shield-x'
    : description.value.status === 0
      ? 'i-lucide-wifi-off'
      : 'i-lucide-triangle-alert',
)
</script>

<template>
  <UAlert
    v-if="compact"
    color="error"
    variant="subtle"
    :icon="icon"
    :title="title ?? description.title"
    :description="description.hint ? `${description.message} ${description.hint}` : description.message"
    :actions="[{ label: 'Retry', color: 'error', variant: 'outline', onClick: () => $emit('retry') }]"
  />
  <EmptyState v-else :icon="icon" :title="title ?? description.title" :description="description.message">
    <p v-if="description.hint" class="w-full text-sm text-muted">{{ description.hint }}</p>
    <UButton label="Retry" icon="i-lucide-rotate-cw" color="neutral" variant="outline" @click="$emit('retry')" />
  </EmptyState>
</template>
