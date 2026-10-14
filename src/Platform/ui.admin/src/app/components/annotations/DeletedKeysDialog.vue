<script setup lang="ts">
import type { Annotation } from '@42for.net/2splatform.sdk'
import type { DeletedKeyChoice } from '@/composables/useDeletedKeyCheck'

/**
 * Asked before a create touches deleted keys (spec 4.5): restore them, which keeps their configuration and history,
 * or create new ones, which replaces them. Used with useDeletedKeyCheck by every flow that creates keys; a flow that
 * writes nothing over the restored keys (Bulk create) names the action "Restore".
 */
withDefaults(defineProps<{ deleted: Annotation[]; loading?: boolean; restoreLabel?: string }>(), {
  loading: false,
  restoreLabel: 'Restore and update',
})
const emit = defineEmits<{ choose: [choice: DeletedKeyChoice] }>()
const open = defineModel<boolean>('open', { default: false })
</script>

<template>
  <UModal
    v-model:open="open"
    title="Deleted annotations"
    description="These annotations were deleted. Restore them to keep their configuration and history, or create new ones, which replaces them."
    :ui="{ content: 'max-w-2xl', footer: 'justify-end' }"
  >
    <template #body>
      <ul class="divide-y divide-default rounded-md border border-default text-sm" aria-label="Deleted annotations">
        <li v-for="annotation in deleted" :key="annotation.AnnotationKey" class="flex items-start gap-3 px-3 py-2">
          <AnnotationTypeBadge :type="annotation.AnnotationKey.slice(0, 3)" variant="label" />
          <div class="min-w-0 flex-1">
            <KeyText :value="annotation.AnnotationKey" />
            <p v-if="annotation.Title" class="truncate text-xs text-muted">{{ annotation.Title }}</p>
          </div>
          <div v-if="annotation.Deletion" class="shrink-0 text-end text-xs text-muted">
            <p>
              Deleted <RelativeTime :value="annotation.Deletion.DeletedAt" /> by
              <AuditActorLink :actor="annotation.Deletion.DeletedBy" />
            </p>
            <p v-if="annotation.Deletion.IsRoot && annotation.Deletion.DescendantCount">
              with {{ annotation.Deletion.DescendantCount }} descendant{{
                annotation.Deletion.DescendantCount === 1 ? '' : 's'
              }}
            </p>
          </div>
        </li>
      </ul>
    </template>
    <template #footer>
      <UButton label="Cancel" color="neutral" variant="ghost" :disabled="loading" @click="emit('choose', 'cancel')" />
      <UButton
        label="Create new"
        color="neutral"
        variant="outline"
        :disabled="loading"
        @click="emit('choose', 'create')"
      />
      <UButton
        :label="restoreLabel"
        icon="i-lucide-archive-restore"
        :loading="loading"
        autofocus
        @click="emit('choose', 'restore')"
      />
    </template>
  </UModal>
</template>
