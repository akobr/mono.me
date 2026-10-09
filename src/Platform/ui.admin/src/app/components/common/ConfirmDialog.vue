<script setup lang="ts">
import { computed, ref, watch } from 'vue'

/**
 * Confirmation for destructive actions. With `confirmText`, the user must type it (a key, an id or the start
 * of a thumbprint) before the action is enabled.
 */
const props = withDefaults(
  defineProps<{
    title: string
    description?: string
    confirmText?: string
    confirmLabel?: string
    color?: 'error' | 'warning' | 'primary'
    loading?: boolean
  }>(),
  { description: undefined, confirmText: undefined, confirmLabel: 'Delete', color: 'error', loading: false },
)

const emit = defineEmits<{ confirm: [] }>()
const open = defineModel<boolean>('open', { default: false })
const typed = ref('')

watch(open, (value) => value && (typed.value = ''))

const enabled = computed(() => !props.confirmText || typed.value.trim() === props.confirmText)
</script>

<template>
  <UModal v-model:open="open" :title="title" :description="description" :ui="{ footer: 'justify-end' }">
    <template v-if="confirmText || $slots.default" #body>
      <slot />
      <UFormField v-if="confirmText" :label="`Type ${confirmText} to confirm`" class="mt-2">
        <UInput
          v-model="typed"
          class="w-full font-mono"
          autocomplete="off"
          :placeholder="confirmText"
          @keydown.enter="enabled && emit('confirm')"
        />
      </UFormField>
    </template>
    <template #footer>
      <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
      <UButton :label="confirmLabel" :color="color" :disabled="!enabled" :loading="loading" @click="emit('confirm')" />
    </template>
  </UModal>
</template>
