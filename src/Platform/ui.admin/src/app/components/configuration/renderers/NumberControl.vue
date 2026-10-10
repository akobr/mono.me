<script setup lang="ts">
import { computed } from 'vue'
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// Number and integer controls: minimum, maximum and multipleOf (as the step).
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const schema = computed(() => control.value.schema as Record<string, unknown>)
const integer = computed(() => schema.value.type === 'integer')
const current = computed(() => (control.value.data as number | undefined) ?? null)
const minimum = computed(() => schema.value.minimum as number | undefined)
const maximum = computed(() => schema.value.maximum as number | undefined)
const step = computed(() => Number(schema.value.multipleOf ?? (integer.value ? 1 : 0.1)))
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ id, readonly, placeholder }">
      <UInputNumber
        :id="id"
        :model-value="current"
        :disabled="readonly"
        :placeholder="placeholder"
        :min="minimum"
        :max="maximum"
        :step="step"
        :format-options="integer ? { maximumFractionDigits: 0 } : { maximumFractionDigits: 6 }"
        class="w-full max-w-60"
        @update:model-value="(value) => handleChange(control.path, value ?? undefined)"
      />
    </template>
  </ControlField>
</template>
