<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { createDefaultValue, Generate, type ControlElement, type JsonSchema } from '@jsonforms/core'
import { DispatchRenderer, rendererProps, useJsonFormsControl } from '@jsonforms/vue'
import { validateAgainst } from '@/utils/schema-validation'

// oneOf / anyOf of objects: a segmented control picks the branch, then that branch's form (spec section 10).
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const branches = computed(() => {
  const schema = control.value.schema as { oneOf?: JsonSchema[]; anyOf?: JsonSchema[] }
  return (schema.oneOf ?? schema.anyOf ?? []) as (JsonSchema & { title?: string })[]
})

// The first branch the current data fits.
const fitting = () => {
  const index = branches.value.findIndex((branch) => validateAgainst(branch as object, control.value.data).length === 0)
  return index >= 0 ? index : 0
}
const selected = ref(fitting())
watch(
  () => control.value.data,
  () => {
    if (control.value.data === undefined) {
      selected.value = fitting()
    }
  },
)

const tabs = computed(() =>
  branches.value.map((branch, index) => ({ label: branch.title ?? `Option ${index + 1}`, value: index })),
)
const uischema = computed(() =>
  Generate.uiSchema(branches.value[selected.value] ?? {}, 'VerticalLayout', undefined, control.value.rootSchema),
)

function choose(index: number) {
  selected.value = index
  handleChange(control.value.path, createDefaultValue(branches.value[index] ?? {}, control.value.rootSchema))
}
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ readonly }">
      <div class="space-y-3 rounded-md border border-default p-3">
        <UTabs
          :model-value="selected"
          :items="tabs"
          :content="false"
          size="xs"
          :disabled="readonly"
          @update:model-value="(value) => choose(Number(value))"
        />
        <DispatchRenderer
          :schema="branches[selected] ?? {}"
          :uischema="uischema"
          :path="control.path"
          :enabled="control.enabled && !readonly"
          :renderers="control.renderers"
          :cells="control.cells"
        />
      </div>
    </template>
  </ControlField>
</template>
