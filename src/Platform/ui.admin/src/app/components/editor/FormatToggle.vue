<script setup lang="ts">
import { computed } from 'vue'
import type { EditorMode } from '@/stores/preferences'

/** Form | JSON | YAML switch in every editor header (spec section 9.2); Form only where a form exists. */
const props = withDefaults(defineProps<{ modelValue: EditorMode; withForm?: boolean; disabled?: boolean }>(), {
  withForm: false,
  disabled: false,
})
const emit = defineEmits<{ 'update:modelValue': [value: EditorMode] }>()

const options = computed(() => [
  ...(props.withForm ? [{ value: 'form' as const, label: 'Form', icon: 'i-lucide-list' }] : []),
  { value: 'json' as const, label: 'JSON', icon: 'i-lucide-braces' },
  { value: 'yaml' as const, label: 'YAML', icon: 'i-lucide-file-text' },
])
</script>

<template>
  <UFieldGroup size="xs" role="radiogroup" aria-label="Editing mode">
    <UButton
      v-for="option in options"
      :key="option.value"
      :label="option.label"
      :icon="option.icon"
      role="radio"
      :aria-checked="modelValue === option.value"
      :color="modelValue === option.value ? 'primary' : 'neutral'"
      :variant="modelValue === option.value ? 'subtle' : 'outline'"
      :disabled="disabled"
      @click="emit('update:modelValue', option.value)"
    />
  </UFieldGroup>
</template>
