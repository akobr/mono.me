<script setup lang="ts">
import { computed } from 'vue'
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// Arrays of strings, numbers or enum values: tags (spec section 10).
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const items = computed(
  () => ((control.value.schema as { items?: Record<string, unknown> }).items ?? {}) as Record<string, unknown>,
)
const options = computed(() =>
  Array.isArray(items.value.enum) ? (items.value.enum as (string | number)[]) : undefined,
)
const numeric = computed(() => items.value.type === 'number' || items.value.type === 'integer')
const values = computed(() => (Array.isArray(control.value.data) ? (control.value.data as (string | number)[]) : []))

function update(next: unknown) {
  const list = (Array.isArray(next) ? next : []).map((value) => (numeric.value ? Number(value) : value))
  handleChange(control.value.path, list.length ? list : undefined)
}
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ id, readonly, placeholder }">
      <USelectMenu
        v-if="options"
        :id="id"
        :model-value="values"
        :items="options"
        multiple
        :disabled="readonly"
        :placeholder="placeholder ?? 'Select…'"
        class="w-full"
        @update:model-value="update"
      />
      <UInputTags
        v-else
        :id="id"
        :model-value="values.map(String)"
        :disabled="readonly"
        :placeholder="placeholder ?? 'Add a value and press Enter'"
        class="w-full"
        @update:model-value="update"
      />
    </template>
  </ControlField>
</template>
