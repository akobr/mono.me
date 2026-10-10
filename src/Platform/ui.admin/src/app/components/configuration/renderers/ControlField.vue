<script setup lang="ts">
import { computed } from 'vue'
import type { JsonSchema } from '@jsonforms/core'
import { getAt, fromDataPath, hasAt } from '@/utils/json-pointer'
import { isBinding } from '@/utils/bindings'
import { useSchemaForm } from './context'

/**
 * The frame of every schema form control (spec section 10): label with the required mark, Markdown help, errors,
 * the "inherited" chip with Override / Reset to inherited, a deprecated badge, and the literal ⇄ expression ("fx")
 * toggle. A binding value renders the expression input instead of the control (JSON Forms testers cannot see the
 * data, so this is where bindings are detected).
 */
const props = defineProps<{
  control: {
    data: unknown
    path: string
    label: string
    description?: string
    errors: string
    required?: boolean
    enabled: boolean
    schema: JsonSchema
    visible: boolean
  }
  rootData?: unknown
}>()

const emit = defineEmits<{ change: [value: unknown] }>()

const form = useSchemaForm()
const pointer = computed(() => fromDataPath(props.control.path))
const inherited = computed(() => (form ? getAt(form.effective.value, pointer.value) : undefined))
const hasOwn = computed(() => props.control.data !== undefined)
const showInherited = computed(() => !hasOwn.value && inherited.value !== undefined)
const binding = computed(() => isBinding(props.control.data))
const schema = computed(() => (props.control.schema ?? {}) as Record<string, unknown>)
const deprecated = computed(() => schema.value.deprecated === true)
const readonly = computed(() => !props.control.enabled || schema.value.readOnly === true || form?.readonly.value)

const inheritedText = computed(() => {
  const value = inherited.value
  return value === undefined ? undefined : typeof value === 'string' ? value : JSON.stringify(value)
})

function toExpression() {
  emit('change', `@${typeof props.control.data === 'string' ? props.control.data : ''}`)
}

const canReset = computed(() => hasOwn.value && form !== undefined && hasAt(form.effective.value, pointer.value))
</script>

<template>
  <div v-if="control.visible" class="space-y-1.5" :data-path="pointer">
    <div class="flex flex-wrap items-center gap-1.5">
      <label class="text-sm font-medium text-highlighted" :for="pointer">
        {{ control.label }}<span v-if="control.required" class="text-error"> *</span>
      </label>
      <UBadge v-if="deprecated" label="Deprecated" color="warning" variant="subtle" size="sm" />
      <UBadge
        v-if="showInherited"
        label="inherited"
        icon="i-lucide-corner-left-down"
        color="neutral"
        variant="outline"
        size="sm"
      />
      <div class="ms-auto flex items-center gap-1">
        <UButton
          v-if="showInherited && !readonly"
          label="Override"
          size="xs"
          color="neutral"
          variant="ghost"
          @click="form?.override(pointer, inherited)"
        />
        <UButton
          v-if="canReset && !readonly"
          label="Reset to inherited"
          size="xs"
          color="neutral"
          variant="ghost"
          @click="form?.reset(pointer)"
        />
        <UTooltip v-if="!readonly && !binding" text="Use a binding expression (@…)">
          <UButton
            label="fx"
            size="xs"
            color="neutral"
            variant="ghost"
            class="font-mono italic"
            @click="toExpression"
          />
        </UTooltip>
      </div>
    </div>

    <BindingExpressionInput
      v-if="binding"
      :id="pointer"
      :model-value="control.data"
      :disabled="readonly"
      @update:model-value="(value) => emit('change', value)"
    />
    <slot v-else :id="pointer" :readonly="readonly" :placeholder="showInherited ? inheritedText : undefined" />

    <MarkdownText v-if="control.description" :source="control.description" class="text-xs text-muted" />
    <p v-if="control.errors" class="text-xs text-error" role="alert">{{ control.errors }}</p>
  </div>
</template>
