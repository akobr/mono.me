<script setup lang="ts">
/**
 * New annotation and Bulk create of the Annotations and Graph pages (424-130), for Contributors and higher roles:
 * in the top bar (`header`) or in the empty state of a view (`empty`). They open the dialogs through their shared
 * state; the page mounts each dialog once and decides what follows a create.
 */
defineProps<{ variant: 'header' | 'empty' }>()

const canEdit = useCan('edit')
const create = useAnnotationCreate()
const bulkCreate = useAnnotationBulkCreate()
</script>

<template>
  <template v-if="canEdit">
    <template v-if="variant === 'header'">
      <!-- Not below sm, where the navbar has no room for a third button: the command palette and the empty
           state open it there. Icon-only until lg, where the sidebar is collapsed and the label would push the
           right group over the breadcrumb. -->
      <UTooltip text="Bulk create annotations">
        <UButton
          label="Bulk create"
          aria-label="Bulk create"
          icon="i-lucide-list-plus"
          color="neutral"
          variant="outline"
          class="hidden sm:inline-flex"
          :ui="{ label: 'hidden lg:inline' }"
          @click="bulkCreate.show()"
        />
      </UTooltip>
      <UButton label="New" icon="i-lucide-plus" class="hidden sm:inline-flex" @click="create.show()" />
      <UButton icon="i-lucide-plus" aria-label="New annotation" class="sm:hidden" @click="create.show()" />
    </template>
    <template v-else>
      <UButton label="New annotation" icon="i-lucide-plus" @click="create.show()" />
      <UButton
        label="Bulk create"
        icon="i-lucide-list-plus"
        color="neutral"
        variant="outline"
        @click="bulkCreate.show()"
      />
    </template>
  </template>
</template>
