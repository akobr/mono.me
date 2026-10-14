<script setup lang="ts">
import { computed } from 'vue'
import { isBindingString, isEnvelope } from '@/utils/bindings'
import { explainBinding } from '@/utils/binding-language'
import { injectBindingAssist } from '@/composables/useBindingAssist'
import { bindingDecorations } from '@/components/editor/setup/bindings'

/**
 * A binding value in the schema form: an "@…" reference edited as monospace text, or a JSON Logic / JSON-e envelope
 * edited as JSON. The "fx" badge opens the binding reference, whose **Use** replaces the value, and the line under
 * the input explains the expression (424-10). "Convert to value" turns it back into a literal.
 */
const model = defineModel<unknown>()
defineProps<{ id?: string; disabled?: boolean }>()

const assist = injectBindingAssist()
const reference = computed(() => isBindingString(model.value))
const envelope = computed(() => isEnvelope(model.value))
const explanation = computed(() =>
  reference.value ? explainBinding(model.value as string, assist.catalog.value) : undefined,
)
const heading = computed(() =>
  explanation.value ? `${explanation.value.title}${explanation.value.name ? ` ${explanation.value.name}` : ''}:` : '',
)
const extensions = [bindingDecorations(assist)]

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
        <BindingReference :insertable="!disabled" @insert="(expression: string) => (model = expression)">
          <UButton
            label="fx"
            size="xs"
            color="info"
            variant="link"
            class="px-0 font-mono font-semibold italic"
            aria-label="Binding reference"
          />
        </BindingReference>
      </template>
    </UInput>
    <DocumentEditor
      v-else-if="envelope"
      :model-value="model"
      format="json"
      :readonly="disabled"
      hide-toggle
      :height="160"
      :extensions="extensions"
      aria-label="Binding envelope"
      @update:model-value="(value) => (model = value)"
    />
    <div v-if="explanation" class="text-xs" data-binding-explanation>
      <span class="font-medium text-highlighted">{{ heading }}</span>
      {{ ' ' }}
      <MarkdownText
        v-if="explanation.problem"
        :source="explanation.problem"
        class="inline text-warning [&>p]:inline"
        role="status"
      />
      <MarkdownText v-else :source="explanation.summary" class="inline text-muted [&>p]:inline" />
    </div>
    <div class="flex items-center gap-2 text-xs text-muted">
      <UIcon name="i-lucide-function-square" class="size-3.5 text-info" />
      <span>Evaluated by the server; schema checks run after evaluation.</span>
      <UButton v-if="!disabled" label="Convert to value" size="xs" color="neutral" variant="link" @click="toLiteral" />
    </div>
  </div>
</template>
