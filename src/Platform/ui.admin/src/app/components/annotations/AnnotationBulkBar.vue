<script setup lang="ts">
import { computed, ref } from 'vue'
import { useCatalogStore } from '@/stores/catalog'
import { errorMessage } from '@/utils/errors'

/** Bulk actions for the selected rows (Contributor and above): enable, disable, add or remove a label, delete. */
const props = defineProps<{ keys: string[] }>()
const emit = defineEmits<{ clear: [] }>()

const catalog = useCatalogStore()
const actions = useAnnotationActions()
const notifyDeleted = useDeletionToast()
const toast = useToast()

const label = ref<string>()
const confirmDelete = ref(false)
const busy = ref(false)

const labelItems = computed(() => [...catalog.labelCounts.keys()])
const formatter = new Intl.NumberFormat()
const count = computed(() => formatter.format(props.keys.length))

async function run(title: string, action: () => Promise<unknown>) {
  busy.value = true
  try {
    await action()
    toast.add({ title, color: 'success', icon: 'i-lucide-check' })
    emit('clear')
  } catch (error) {
    toast.add({ title: 'The bulk action failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

async function deleteSelected() {
  busy.value = true
  try {
    const { failed, deletions } = await actions.removeMany(props.keys)
    confirmDelete.value = false
    if (failed.length) {
      toast.add({
        title: `${failed.length} annotations could not be deleted`,
        description: failed.slice(0, 5).join(', '),
        color: 'warning',
      })
    }
    // Undo restores every root of the batch with its descendants, 100 keys per request.
    const deleted = props.keys.length - failed.length
    if (deleted > 0) {
      notifyDeleted(`${formatter.format(deleted)} annotations deleted`, deletions)
    }
    emit('clear')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div
    class="flex flex-wrap items-center gap-2 border-b border-default bg-elevated/50 px-4 py-2 text-sm"
    role="toolbar"
    aria-label="Bulk actions"
  >
    <span class="font-medium">{{ count }} selected</span>
    <UButton
      label="Enable"
      icon="i-lucide-circle-check"
      size="xs"
      color="neutral"
      variant="outline"
      :disabled="busy"
      @click="run('Annotations enabled', () => actions.setDisabled(keys, false))"
    />
    <UButton
      label="Disable"
      icon="i-lucide-circle-slash"
      size="xs"
      color="neutral"
      variant="outline"
      :disabled="busy"
      @click="run('Annotations disabled', () => actions.setDisabled(keys, true))"
    />
    <UPopover>
      <UButton label="Label" icon="i-lucide-tag" size="xs" color="neutral" variant="outline" :disabled="busy" />
      <template #content>
        <div class="flex w-64 flex-col gap-2 p-3">
          <UInputMenu
            v-model="label"
            :items="labelItems"
            create-item
            placeholder="Label"
            @create="(value: string) => (label = value)"
          />
          <div class="flex justify-end gap-2">
            <UButton
              label="Remove"
              size="xs"
              color="neutral"
              variant="outline"
              :disabled="!label || busy"
              @click="run(`Label ${label} removed`, () => actions.removeLabel(keys, label!))"
            />
            <UButton
              label="Add"
              size="xs"
              :disabled="!label || busy"
              @click="run(`Label ${label} added`, () => actions.addLabel(keys, label!))"
            />
          </div>
        </div>
      </template>
    </UPopover>
    <UButton
      label="Delete"
      icon="i-lucide-trash-2"
      size="xs"
      color="error"
      variant="outline"
      :disabled="busy"
      @click="confirmDelete = true"
    />
    <UProgress
      v-if="actions.progress.value"
      class="w-32"
      size="sm"
      :model-value="actions.progress.value.done"
      :max="actions.progress.value.total"
    />
    <UButton class="ms-auto" label="Clear selection" size="xs" color="neutral" variant="ghost" @click="emit('clear')" />

    <ConfirmDialog
      v-model:open="confirmDelete"
      :title="`Delete ${count} annotations?`"
      :description="`The ${count} annotations and their descendants move to Deleted, with their configuration. You can restore them from Deleted for 90 days.`"
      :confirm-label="`Delete ${count}`"
      :loading="busy"
      @confirm="deleteSelected"
    />
  </div>
</template>
