<script setup lang="ts">
import { computed } from 'vue'
import { isBindingString, isEnvelope } from '@/utils/bindings'

/**
 * A binding value in the schema form: an "@…" reference edited as monospace text with an "fx" badge, or a
 * JSON Logic / JSON-e envelope edited as JSON. "Convert to value" turns it back into a literal.
 */
const model = defineModel<unknown>()
defineProps<{ id?: string; disabled?: boolean }>()

const reference = computed(() => isBindingString(model.value))
const envelope = computed(() => isEnvelope(model.value))

function toLiteral() {
  model.value = reference.value ? String(model.value).slice(1) : undefined
}
</script>

<template>
  <div class="space-y-1">
    <UInput
      v-if="reference"
      :id="id"
      :model-value="model as string"
      :disabled="disabled"
      class="w-full"
      :ui="{ base: 'font-mono' }"
      @update:model-value="(value) => (model = String(value).startsWith('@') ? value : `@${value}`)"
    >
      <template #leading>
        <span class="font-mono text-xs font-semibold text-info italic">fx</span>
      </template>
    </UInput>
    <DocumentEditor
      v-else-if="envelope"
      :model-value="model"
      format="json"
      :readonly="disabled"
      hide-toggle
      :height="160"
      aria-label="Binding envelope"
      @update:model-value="(value) => (model = value)"
    />
    <div class="flex items-center gap-2 text-xs text-muted">
      <UIcon name="i-lucide-function-square" class="size-3.5 text-info" />
      <span>Evaluated by the server; schema checks run after evaluation.</span>
      <UButton v-if="!disabled" label="Convert to value" size="xs" color="neutral" variant="link" @click="toLiteral" />
    </div>
  </div>
</template>
