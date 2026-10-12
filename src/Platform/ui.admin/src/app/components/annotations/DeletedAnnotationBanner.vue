<script setup lang="ts">
import { computed, ref } from 'vue'
import type { DropdownMenuItem } from '@nuxt/ui'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { formatDate } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

/**
 * A deleted annotation (spec 4.3, 4.5): when and by whom it was deleted, how many descendants went with it, the
 * date of the automatic purge, and Restore for Contributors.
 */
const props = defineProps<{ annotation: Annotation }>()
const emit = defineEmits<{ restored: [] }>()

const actions = useAnnotationActions()
const canEdit = useCan('edit')
const toast = useToast()
const busy = ref(false)

const deletion = computed(() => props.annotation.Deletion)
const descendants = computed(() => deletion.value?.DescendantCount ?? 0)

async function restore(descendantsToo: boolean) {
  busy.value = true
  try {
    const outcome = await actions.restoreMany([props.annotation.AnnotationKey], { descendants: descendantsToo })
    if (outcome.failed.length) {
      throw new Error(outcome.failed.map((failure) => failure.Message).join(' '))
    }
    toast.add({
      title: `${props.annotation.AnnotationKey} restored`,
      color: 'success',
      icon: 'i-lucide-archive-restore',
    })
    emit('restored')
  } catch (error) {
    toast.add({ title: 'Restore failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

const menu = computed<DropdownMenuItem[]>(() => [
  { label: 'Restore without descendants', icon: 'i-lucide-archive-restore', onSelect: () => void restore(false) },
])
</script>

<template>
  <UAlert
    color="warning"
    variant="subtle"
    icon="i-lucide-archive"
    :title="`${annotation.AnnotationKey} is deleted`"
    data-testid="deleted-banner"
  >
    <template #description>
      <p v-if="deletion">
        Deleted on {{ deletion.DeletedAt ? formatDate(new Date(deletion.DeletedAt)) : '–' }} by
        <AuditActorLink :actor="deletion.DeletedBy" />
        <template v-if="deletion.IsRoot && descendants > 0">
          with {{ descendants }} descendant{{ descendants === 1 ? '' : 's' }}</template
        ><template v-else-if="!deletion.IsRoot && deletion.RootKey"> together with {{ deletion.RootKey }}</template
        >.
        <template v-if="deletion.PurgeAt">
          Purged automatically on {{ formatDate(new Date(deletion.PurgeAt)) }}.
        </template>
      </p>
    </template>
    <template v-if="canEdit" #actions>
      <UFieldGroup>
        <UButton label="Restore" icon="i-lucide-archive-restore" size="xs" :loading="busy" @click="restore(true)" />
        <UDropdownMenu :items="menu" :content="{ align: 'end' }">
          <UButton icon="i-lucide-chevron-down" size="xs" :disabled="busy" aria-label="More restore options" />
        </UDropdownMenu>
      </UFieldGroup>
    </template>
  </UAlert>
</template>
