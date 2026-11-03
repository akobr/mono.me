<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  deleteKeyValue,
  protectKeyValues,
  purgeKeyValues,
  restoreKeyValues,
  type KeyValueBulkItemResult,
  type KeyValueProtection,
} from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { errorMessage, writeFailureTitle } from '@/utils/errors'
import { levelMeta, serverSideLevels } from '@/utils/key-values'

/**
 * Actions for the selected keys (spec 29.2): Delete and Change protection in the Active state, Restore and Purge
 * in the Deleted state. One bulk call each; the outcome is reported per key and the failed keys stay selected. A
 * selection of active and deleted keys (the All state of the list) has no actions, as a bulk call takes one state.
 */
const props = defineProps<{ keys: string[]; state: 'Active' | 'Deleted' | 'Mixed'; allowedLevels: KeyValueProtection[]; retentionDays?: number }>()
const emit = defineEmits<{ done: [failed: string[]] }>()

const context = useContextStore()
const toast = useToast()
const confirm = useConfirm()
const canSecrets = useCan('secrets')
const canAdmin = useCan('manageAccess')

const busy = ref(false)
const purging = ref(false)
const count = computed(() => new Intl.NumberFormat().format(props.keys.length))

function report(title: string, items: KeyValueBulkItemResult[] | undefined): string[] {
  const failed = (items ?? []).filter((item) => item.Outcome !== 'Done' && item.Outcome !== 'Unchanged')
  if (failed.length === 0) {
    toast.add({ title, color: 'success', icon: 'i-lucide-check' })
  } else {
    toast.add({
      title: `${failed.length} of ${items?.length ?? 0} keys were not done`,
      description: failed
        .slice(0, 5)
        .map((item) => `${item.Key}: ${item.Message ?? item.Outcome}`)
        .join('; '),
      color: 'warning',
    })
  }
  return failed.map((item) => item.Key ?? '').filter(Boolean)
}

// The API takes at most 100 keys per bulk call; a batch that fails as a whole reports each of its keys as failed.
const bulkLimit = 100

async function inBatches(call: (keys: string[]) => Promise<{ Items?: KeyValueBulkItemResult[] }>): Promise<KeyValueBulkItemResult[]> {
  // the keys selected when the action started: the selection may change while the batches run
  const keys = [...props.keys]
  const items: KeyValueBulkItemResult[] = []
  for (let offset = 0; offset < keys.length; offset += bulkLimit) {
    const batch = keys.slice(offset, offset + bulkLimit)
    try {
      items.push(...((await call(batch)).Items ?? []))
    } catch (error) {
      items.push(...batch.map((key): KeyValueBulkItemResult => ({ Key: key, Outcome: 'Failed', Message: errorMessage(error) })))
    }
  }
  return items
}

async function run(action: () => Promise<string[]>) {
  busy.value = true
  try {
    emit('done', await action())
  } catch (error) {
    toast.add({ title: writeFailureTitle(error, 'The bulk action failed'), description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

async function remove() {
  const days = props.retentionDays ?? 90
  const confirmed = await confirm({
    title: `Delete ${count.value} key(s)?`,
    description: `Deleted keys can be restored for ${days} days, with every version; after that they are removed for good.`,
    confirmLabel: 'Delete',
    color: 'error',
  })
  if (!confirmed || !context.scope) {
    return
  }
  const { org, project, view } = context.scope
  await run(async () => {
    const failed: string[] = []
    for (const key of props.keys) {
      try {
        await deleteKeyValue(org, project, view, key)
      } catch {
        failed.push(key)
      }
    }
    if (failed.length) {
      toast.add({ title: `${failed.length} key(s) could not be deleted`, description: failed.slice(0, 5).join(', '), color: 'warning' })
    } else {
      toast.add({ title: `${count.value} key(s) deleted`, color: 'success', icon: 'i-lucide-trash-2' })
    }
    return failed
  })
}

async function protect(level: KeyValueProtection) {
  const confirmed = await confirm({
    title: `Move ${count.value} key(s) to ${levelMeta[level].label}?`,
    description: 'Each value is encrypted again under the new level as a new version; older versions keep their level until they expire. Public and HYOK keys are skipped.',
    confirmLabel: 'Change protection',
    color: 'primary',
  })
  if (!confirmed || !context.scope) {
    return
  }
  const { org, project, view } = context.scope
  await run(async () => report(`${count.value} key(s) moved to ${levelMeta[level].label}`, await inBatches((keys) => protectKeyValues(org, project, view, { Keys: keys, Protection: level }))))
}

async function restore() {
  if (!context.scope) {
    return
  }
  const { org, project, view } = context.scope
  await run(async () => report(`${count.value} key(s) restored`, await inBatches((keys) => restoreKeyValues(org, project, view, { Keys: keys }))))
}

async function purge() {
  if (!context.scope) {
    return
  }
  const { org, project, view } = context.scope
  purging.value = false
  await run(async () => report(`${count.value} key(s) purged`, await inBatches((keys) => purgeKeyValues(org, project, view, { Keys: keys }))))
}

const protectItems = computed(() =>
  serverSideLevels
    .filter((level) => props.allowedLevels.includes(level))
    .map((level) => ({ label: levelMeta[level].label, icon: levelMeta[level].icon, onSelect: () => protect(level) })),
)
</script>

<template>
  <div class="flex flex-wrap items-center gap-2 border-b border-default bg-elevated/50 px-4 py-2 text-sm" role="toolbar" aria-label="Bulk actions">
    <span class="font-medium">{{ count }} selected</span>
    <template v-if="state === 'Active'">
      <UButton label="Delete" icon="i-lucide-trash-2" color="error" variant="soft" size="sm" :loading="busy" @click="remove" />
      <UDropdownMenu v-if="canSecrets && protectItems.length" :items="protectItems">
        <UButton label="Change protection" icon="i-lucide-arrow-right-left" color="neutral" variant="soft" size="sm" :loading="busy" />
      </UDropdownMenu>
    </template>
    <span v-else-if="state === 'Mixed'" class="text-muted">Select only active or only deleted keys for bulk actions.</span>
    <template v-else>
      <UButton label="Restore" icon="i-lucide-undo-2" color="primary" variant="soft" size="sm" :loading="busy" @click="restore" />
      <UButton v-if="canAdmin" label="Purge" icon="i-lucide-flame" color="error" variant="soft" size="sm" :loading="busy" @click="purging = true" />
    </template>
    <UButton label="Clear selection" color="neutral" variant="ghost" size="sm" class="ms-auto" @click="emit('done', [])" />

    <ConfirmDialog
      v-model:open="purging"
      title="Purge the selected keys?"
      :description="`${count} deleted key(s) and every version of them are removed for good, before their retention ends.`"
      :confirm-text="context.view"
      confirm-label="Purge"
      :loading="busy"
      @confirm="purge"
    />
  </div>
</template>
