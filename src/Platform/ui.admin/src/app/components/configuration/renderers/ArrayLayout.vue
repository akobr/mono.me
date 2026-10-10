<script setup lang="ts">
import { computed, ref } from 'vue'
import { composePaths, createDefaultValue, Generate, type ControlElement, type JsonSchema } from '@jsonforms/core'
import { DispatchRenderer, rendererProps, useJsonFormsArrayControl } from '@jsonforms/vue'

// Arrays of objects: collapsible cards with add, remove and move up/down; titles from `title` or the first string
// property of the item (spec section 10).
const props = defineProps(rendererProps<ControlElement>())
const { control, addItem, removeItems, moveUp, moveDown } = useJsonFormsArrayControl(props)

const items = computed(() =>
  Array.isArray(control.value.data) ? (control.value.data as Record<string, unknown>[]) : [],
)
const childSchema = computed(() => control.value.schema as JsonSchema)
const childUiSchema = computed(() =>
  Generate.uiSchema(childSchema.value, 'VerticalLayout', undefined, control.value.rootSchema),
)
const open = ref(new Set<number>([0]))

function title(item: Record<string, unknown>, index: number): string {
  const first = Object.entries(item ?? {}).find(([, value]) => typeof value === 'string' && value)
  return first ? String(first[1]) : `Item ${index + 1}`
}

function toggle(index: number) {
  const next = new Set(open.value)
  if (next.has(index)) {
    next.delete(index)
  } else {
    next.add(index)
  }
  open.value = next
}

function add() {
  addItem(control.value.path, createDefaultValue(childSchema.value, control.value.rootSchema))()
  open.value = new Set([...open.value, items.value.length])
}
</script>

<template>
  <ControlField :control="control" @change="(value) => addItem(control.path, value)">
    <template #default="{ readonly }">
      <div class="space-y-2">
        <div v-for="(item, index) in items" :key="index" class="rounded-md border border-default">
          <div class="flex items-center gap-1 px-2 py-1">
            <UButton
              :icon="open.has(index) ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'"
              :label="title(item, index)"
              color="neutral"
              variant="ghost"
              size="xs"
              class="min-w-0 flex-1 justify-start"
              :ui="{ label: 'truncate' }"
              @click="toggle(index)"
            />
            <template v-if="!readonly">
              <UButton
                icon="i-lucide-arrow-up"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Move up"
                :disabled="index === 0"
                @click="moveUp?.(control.path, index)()"
              />
              <UButton
                icon="i-lucide-arrow-down"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Move down"
                :disabled="index === items.length - 1"
                @click="moveDown?.(control.path, index)()"
              />
              <UButton
                icon="i-lucide-trash-2"
                color="neutral"
                variant="ghost"
                size="xs"
                aria-label="Remove"
                @click="removeItems?.(control.path, [index])()"
              />
            </template>
          </div>
          <div v-if="open.has(index)" class="border-t border-default p-3">
            <DispatchRenderer
              :schema="childSchema"
              :uischema="childUiSchema"
              :path="composePaths(control.path, `${index}`)"
              :enabled="control.enabled && !readonly"
              :renderers="control.renderers"
              :cells="control.cells"
            />
          </div>
        </div>
        <UButton
          v-if="!readonly"
          label="Add item"
          icon="i-lucide-plus"
          size="xs"
          color="neutral"
          variant="outline"
          @click="add"
        />
      </div>
    </template>
  </ControlField>
</template>
