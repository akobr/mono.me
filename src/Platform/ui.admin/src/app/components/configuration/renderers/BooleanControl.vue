<script setup lang="ts">
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ id, readonly, placeholder }">
      <div class="flex items-center gap-2">
        <USwitch
          :id="id"
          :model-value="(control.data as boolean | undefined) ?? false"
          :disabled="readonly"
          @update:model-value="(value) => handleChange(control.path, value)"
        />
        <span v-if="placeholder !== undefined" class="text-xs text-muted">inherits {{ placeholder }}</span>
      </div>
    </template>
  </ControlField>
</template>
