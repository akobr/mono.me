<script setup lang="ts">
defineProps<{ checked: boolean | 'indeterminate'; disabled?: boolean; label: string }>()
const emit = defineEmits<{ 'update:checked': [value: boolean] }>()
</script>

<template>
  <UCheckbox
    :model-value="checked"
    :disabled="disabled"
    :aria-label="label"
    @update:model-value="(value) => emit('update:checked', value === true)"
    @click.stop
  />
</template>
