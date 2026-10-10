<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue'
import { validateSchemaDocument } from '@/utils/schema-validation'

const SchemaForm = defineAsyncComponent(() => import('@/components/configuration/SchemaForm.vue'))

/**
 * The form a schema produces, rendered live while the schema is edited (spec section 14). The sample data is
 * local to the preview and never saved; it stays across schema edits until "Reset sample".
 */
const props = defineProps<{ schema: unknown }>()

const sample = ref<unknown>({})
const valid = computed(() => validateSchemaDocument(props.schema).length === 0)
const renderable = computed(
  () =>
    valid.value &&
    !!props.schema &&
    typeof props.schema === 'object' &&
    (props.schema as { type?: unknown }).type !== 'array',
)
</script>

<template>
  <div class="space-y-3">
    <div class="flex items-center justify-between gap-2">
      <h3 class="text-sm font-semibold text-highlighted">Form preview</h3>
      <UButton label="Reset sample" size="xs" color="neutral" variant="ghost" @click="sample = {}" />
    </div>
    <EmptyState
      v-if="!renderable"
      icon="i-lucide-file-warning"
      title="No preview"
      description="The preview appears when the schema is a valid object schema."
    />
    <div v-else class="rounded-md border border-default p-3">
      <SchemaForm v-model="sample" :schema="schema as object" />
    </div>
    <details v-if="renderable" class="text-xs text-muted">
      <summary class="cursor-pointer">Sample data</summary>
      <pre class="mt-1 overflow-auto rounded bg-elevated p-2 font-mono">{{ JSON.stringify(sample, null, 2) }}</pre>
    </details>
  </div>
</template>
