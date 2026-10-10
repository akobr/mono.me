<script setup lang="ts">
import { computed, ref } from 'vue'
import { findUISchema, type ControlElement, type GroupLayout } from '@jsonforms/core'
import { DispatchRenderer, rendererProps, useJsonFormsControlWithDetail } from '@jsonforms/vue'
import { isEnvelope } from '@/utils/bindings'

// Objects: a collapsible fieldset with title and description (spec section 10). A binding envelope at an object
// path renders the expression editor instead.
const props = defineProps(rendererProps<ControlElement>())
const { control, handleChange } = useJsonFormsControlWithDetail(props)

const collapsed = ref(false)
const detail = computed(
  () =>
    findUISchema(
      control.value.uischemas,
      control.value.schema,
      control.value.uischema.scope,
      control.value.path,
      'VerticalLayout',
      control.value.uischema,
      control.value.rootSchema,
    ) as GroupLayout,
)
const envelope = computed(() => isEnvelope(control.value.data))
</script>

<template>
  <fieldset v-if="control.visible" class="space-y-3 rounded-md border border-default p-3">
    <legend class="flex items-center gap-1 px-1">
      <UButton
        :icon="collapsed ? 'i-lucide-chevron-right' : 'i-lucide-chevron-down'"
        :label="control.label"
        color="neutral"
        variant="ghost"
        size="xs"
        class="font-semibold"
        @click="collapsed = !collapsed"
      />
    </legend>
    <template v-if="!collapsed">
      <MarkdownText v-if="control.description" :source="control.description" class="text-xs text-muted" />
      <BindingExpressionInput
        v-if="envelope"
        :model-value="control.data"
        :disabled="!control.enabled"
        @update:model-value="(value) => handleChange(control.path, value)"
      />
      <DispatchRenderer
        v-else
        :visible="control.visible"
        :enabled="control.enabled"
        :schema="control.schema"
        :uischema="detail"
        :path="control.path"
        :renderers="control.renderers"
        :cells="control.cells"
      />
    </template>
  </fieldset>
</template>
