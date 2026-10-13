<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { rotateKeyValueKey, type KeyValuePolicy, type KeyValueProtection, type ProtectionKeyInfo } from '@42for.net/2splatform.sdk'
import { invalidateKeyValuePolicy } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

/** The Standard and HSM key-encryption keys (spec 29.6): id, version, next rotation, Rotate now, the re-wrap. */
defineProps<{ policy: KeyValuePolicy; editable: boolean }>()

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const confirm = useConfirm()
const rotating = ref<KeyValueProtection>()

async function rotate(level: 'Standard' | 'Hsm') {
  if (!context.org || !context.project) {
    return
  }
  const confirmed = await confirm({
    title: `Rotate the ${level === 'Hsm' ? 'HSM' : 'Standard'} key now?`,
    description: 'New values use the new key version at once; the stored values are re-wrapped in the background. The old version stays usable until the re-wrap is complete.',
    confirmLabel: 'Rotate',
    color: 'primary',
  })
  if (!confirmed) {
    return
  }
  rotating.value = level
  try {
    await rotateKeyValueKey(context.org, context.project, { Protection: level })
    await invalidateKeyValuePolicy(queryClient, context.org, context.project)
    toast.add({ title: `${level === 'Hsm' ? 'HSM' : 'Standard'} key rotated`, color: 'success', icon: 'i-lucide-refresh-cw' })
  } catch (error) {
    toast.add({ title: 'The key was not rotated', description: errorMessage(error), color: 'error' })
  } finally {
    rotating.value = undefined
  }
}

const shortId = (key?: ProtectionKeyInfo) => key?.KeyId?.split('/').slice(-1)[0] ?? '—'
const when = (time?: string | null) => (time ? formatDateTime(new Date(time)) : '—')
const rewrapProgress = computed(() => undefined)
</script>

<template>
  <UCard>
    <template #header><h2 class="font-semibold">Key-encryption keys</h2></template>
    <div class="space-y-4 text-sm">
      <div v-for="entry in [{ level: 'Standard' as const, key: policy.Standard, label: 'Standard' }, { level: 'Hsm' as const, key: policy.Hsm?.Key, label: 'HSM' }]" :key="entry.level" class="flex flex-wrap items-start gap-3">
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <KeyValueLevelBadge :level="entry.level" />
            <span v-if="entry.level === 'Hsm' && policy.Hsm" class="text-xs text-muted">{{ policy.Hsm.Backend === 'ManagedHsm' ? 'Managed HSM' : 'Key Vault Premium' }}</span>
          </div>
          <dl v-if="entry.key" class="mt-1 grid grid-cols-[8rem_minmax(0,1fr)] gap-x-3 gap-y-0.5 text-xs">
            <dt class="text-muted">Key</dt>
            <dd class="truncate font-mono" :title="entry.key.KeyId">{{ shortId(entry.key) }}</dd>
            <dt class="text-muted">Current version</dt>
            <dd class="font-mono">{{ entry.key.CurrentKeyVersion }}</dd>
            <dt class="text-muted">Next rotation</dt>
            <dd>{{ when(entry.key.NextRotationAt) }}</dd>
          </dl>
          <p v-else class="mt-1 text-xs text-muted">
            {{ entry.level === 'Hsm' ? 'Created when the HSM level is allowed.' : 'Created with the first secret, or by the daily check.' }}
          </p>
        </div>
        <UButton
          v-if="editable && entry.key"
          label="Rotate now"
          icon="i-lucide-refresh-cw"
          color="neutral"
          variant="outline"
          size="sm"
          :loading="rotating === entry.level"
          @click="rotate(entry.level)"
        />
      </div>

      <UAlert
        v-if="policy.Rewrap"
        :color="policy.Rewrap.CompletedAt ? 'success' : 'info'"
        variant="subtle"
        :icon="policy.Rewrap.CompletedAt ? 'i-lucide-check' : 'i-lucide-loader'"
        :title="policy.Rewrap.CompletedAt ? `Re-wrap of the ${policy.Rewrap.Protection} values complete` : `Re-wrapping the ${policy.Rewrap.Protection} values`"
        :description="`${policy.Rewrap.Processed ?? 0} processed, ${policy.Rewrap.Remaining ?? 0} remaining; target key version ${policy.Rewrap.TargetKeyVersion}${policy.Rewrap.LastError ? `; last error: ${policy.Rewrap.LastError}` : ''}.`"
      />
      <span v-if="rewrapProgress" class="hidden" />
    </div>
  </UCard>
</template>
