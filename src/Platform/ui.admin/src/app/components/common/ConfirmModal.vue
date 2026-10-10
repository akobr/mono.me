<script setup lang="ts">
/** Promise-based confirmation, opened through useConfirm() (useOverlay). Emits close(true | false). */
withDefaults(
  defineProps<{
    title: string
    description?: string
    confirmLabel?: string
    cancelLabel?: string
    color?: 'error' | 'warning' | 'primary'
  }>(),
  { description: undefined, confirmLabel: 'Confirm', cancelLabel: 'Cancel', color: 'primary' },
)

const emit = defineEmits<{ close: [confirmed: boolean] }>()
</script>

<template>
  <UModal
    :title="title"
    :description="description"
    :ui="{ footer: 'justify-end' }"
    @update:open="(open) => !open && emit('close', false)"
  >
    <template #footer>
      <UButton :label="cancelLabel" color="neutral" variant="ghost" @click="emit('close', false)" />
      <UButton :label="confirmLabel" :color="color" autofocus @click="emit('close', true)" />
    </template>
  </UModal>
</template>
