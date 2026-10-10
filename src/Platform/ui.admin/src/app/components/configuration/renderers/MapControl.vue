<script setup lang="ts">
import { computed, ref } from 'vue'
import { composePaths, createDefaultValue, type ControlElement, type JsonSchema } from '@jsonforms/core'
import { DispatchRenderer, rendererProps, useJsonFormsControl } from '@jsonforms/vue'

// Objects with `additionalProperties` and no `properties`: key/value rows, each value rendered with the
// additionalProperties schema (spec section 10).
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControl(props)

const valueSchema = computed(() => {
  const additional = (control.value.schema as { additionalProperties?: unknown }).additionalProperties
  return (typeof additional === 'object' && additional ? additional : {}) as JsonSchema
})
const entries = computed(() => Object.keys((control.value.data as Record<string, unknown>) ?? {}))
const newKey = ref('')
const valueUiSchema: ControlElement = { type: 'Control', scope: '#' }

function rename(oldKey: string, nextKey: string) {
  const trimmed = nextKey.trim()
  if (!trimmed || trimmed === oldKey || entries.value.includes(trimmed)) {
    return
  }
  const data = (control.value.data as Record<string, unknown>) ?? {}
  handleChange(
    control.value.path,
    Object.fromEntries(Object.entries(data).map(([key, value]) => [key === oldKey ? trimmed : key, value])),
  )
}

function remove(key: string) {
  const { [key]: _removed, ...rest } = (control.value.data as Record<string, unknown>) ?? {}
  handleChange(control.value.path, Object.keys(rest).length ? rest : undefined)
}

function add() {
  const key = newKey.value.trim()
  if (!key || entries.value.includes(key)) {
    return
  }
  handleChange(control.value.path, {
    ...(control.value.data as Record<string, unknown> | undefined),
    [key]: createDefaultValue(valueSchema.value, control.value.rootSchema),
  })
  newKey.value = ''
}
</script>

<template>
  <ControlField :control="control" @change="(value) => handleChange(control.path, value)">
    <template #default="{ readonly }">
      <div class="space-y-2">
        <div v-for="key in entries" :key="key" class="grid grid-cols-[10rem_minmax(0,1fr)_auto] items-start gap-2">
          <UInput
            :model-value="key"
            size="sm"
            class="font-mono"
            aria-label="Key"
            :disabled="readonly"
            @change="(event: Event) => rename(key, (event.target as HTMLInputElement).value)"
          />
          <DispatchRenderer
            :schema="valueSchema"
            :uischema="valueUiSchema"
            :path="composePaths(control.path, key)"
            :enabled="control.enabled && !readonly"
            :renderers="control.renderers"
            :cells="control.cells"
          />
          <UButton
            v-if="!readonly"
            icon="i-lucide-trash-2"
            size="xs"
            color="neutral"
            variant="ghost"
            :aria-label="`Remove ${key}`"
            @click="remove(key)"
          />
        </div>
        <div v-if="!readonly" class="flex items-center gap-2">
          <UInput
            v-model="newKey"
            size="sm"
            placeholder="New key"
            class="w-40 font-mono"
            @keydown.enter.prevent="add"
          />
          <UButton
            label="Add"
            icon="i-lucide-plus"
            size="xs"
            color="neutral"
            variant="outline"
            :disabled="!newKey.trim()"
            @click="add"
          />
        </div>
      </div>
    </template>
  </ControlField>
</template>
