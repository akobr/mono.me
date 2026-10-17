<script setup lang="ts">
import { computed } from 'vue'
import { useKeyValuePolicy } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'

/** "Customer key unavailable since …" across the values store (spec 29.2), linked to the secrets in Settings. */
const context = useContextStore()
const policy = useKeyValuePolicy()

const byok = computed(() => policy.data.value?.Byok)
const unavailable = computed(() => byok.value?.Health === 'Unavailable')
const since = computed(() => (byok.value?.LastCheckedAt ? formatDateTime(new Date(byok.value.LastCheckedAt)) : 'recently'))
</script>

<template>
  <UAlert
    v-if="unavailable"
    color="error"
    variant="subtle"
    icon="i-lucide-key-square"
    title="Customer key unavailable"
    :description="`Your key could not be used since ${since}${byok?.LastError ? `: ${byok.LastError}` : ''}. BYOK values cannot be read or written until the key is enabled again.`"
    :actions="[{ label: 'Secrets settings', to: { path: `/${context.org}/${context.project}/settings`, hash: '#secrets' }, color: 'error', variant: 'outline' }]"
    class="rounded-none border-x-0 border-t-0"
  />
</template>
