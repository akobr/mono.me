<script setup lang="ts">
import type { ControlElement } from '@jsonforms/core'
import { rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// Everything the form cannot render (not, if/then/else, patternProperties, tuples): an inline JSON editor for
// that subtree (spec section 10).
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ readonly }">
      <DocumentEditor
        :model-value="control.data ?? null"
        format="json"
        :readonly="readonly"
        :height="160"
        hide-toggle
        :aria-label="control.label"
        @update:model-value="(value) => handleChange(control.path, value === null ? undefined : value)"
      />
    </template>
  </ControlField>
</template>
