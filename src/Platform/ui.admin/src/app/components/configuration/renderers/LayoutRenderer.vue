<script setup lang="ts">
import { computed } from 'vue'
import type { Layout } from '@jsonforms/core'
import { DispatchRenderer, rendererProps, useJsonFormsLayout } from '@jsonforms/vue'

// VerticalLayout, HorizontalLayout and Group.
const props = defineProps(rendererProps<Layout>())
const { layout } = useJsonFormsLayout(props)

const horizontal = computed(() => layout.value.uischema.type === 'HorizontalLayout')
const label = computed(() => (layout.value.uischema as { label?: string }).label)
</script>

<template>
  <div v-if="layout.visible" :class="horizontal ? 'grid gap-4 sm:grid-flow-col sm:auto-cols-fr' : 'space-y-4'">
    <p v-if="label" class="text-sm font-semibold text-highlighted">{{ label }}</p>
    <DispatchRenderer
      v-for="(element, index) in layout.uischema.elements"
      :key="`${layout.path}-${index}`"
      :schema="layout.schema"
      :uischema="element"
      :path="layout.path"
      :enabled="layout.enabled"
      :renderers="layout.renderers"
      :cells="layout.cells"
    />
  </div>
</template>
