<script setup lang="ts">
import { computed, toRef, watch } from 'vue'
import { JsonForms } from '@jsonforms/vue'
import { Generate, type JsonSchema } from '@jsonforms/core'
import type { ErrorObject } from 'ajv'
import { cloneJson } from '@/utils/clone'
import { removeAt, setAt } from '@/utils/json-pointer'
import { validateAgainst, type ValidationIssue } from '@/utils/schema-validation'
import { provideSchemaForm } from './renderers/context'
import { nuxtUiRenderers } from './renderers'

/**
 * A form generated from the JSON Schema that applies to a configuration (spec section 10): JSON Forms with the
 * Nuxt UI renderer set. Client validation is our Ajv pass (binding values are skipped); server errors of a 409 are
 * attached to the same controls by JSON Pointer.
 */
const props = withDefaults(
  defineProps<{
    schema: object
    modelValue: unknown
    /** The effective document (ancestors, template and own content merged), for placeholders and Override / Reset to inherited. */
    effective?: unknown
    /** The own content as saved; values it holds are own, not inherited, even though the effective document has them too. */
    own?: unknown
    readonly?: boolean
    serverErrors?: ValidationIssue[]
  }>(),
  { effective: undefined, own: undefined, readonly: false, serverErrors: () => [] },
)

const emit = defineEmits<{ 'update:modelValue': [value: unknown]; 'update:valid': [valid: boolean] }>()

const uischema = computed(() => Generate.uiSchema(props.schema as JsonSchema))
const issues = computed(() => validateAgainst(props.schema, props.modelValue ?? {}))

watch(issues, (value) => emit('update:valid', value.length === 0), { immediate: true })

const additionalErrors = computed<ErrorObject[]>(() =>
  [...issues.value, ...props.serverErrors].map((issue) => ({
    instancePath: issue.pointer,
    message: issue.message,
    keyword: issue.keyword,
    schemaPath: '',
    params: {},
  })),
)

let lastEmitted = JSON.stringify(props.modelValue ?? {})

function onChange(event: { data: unknown }) {
  const text = JSON.stringify(event.data ?? {})
  if (text !== lastEmitted) {
    lastEmitted = text
    emit('update:modelValue', event.data)
  }
}

watch(
  () => props.modelValue,
  (value) => (lastEmitted = JSON.stringify(value ?? {})),
)

provideSchemaForm({
  effective: toRef(props, 'effective'),
  own: toRef(props, 'own'),
  readonly: toRef(props, 'readonly'),
  override: (pointer, value) => emit('update:modelValue', setAt(props.modelValue ?? {}, pointer, cloneJson(value))),
  reset: (pointer) => emit('update:modelValue', removeAt(props.modelValue ?? {}, pointer)),
})
</script>

<template>
  <div class="schema-form space-y-4">
    <JsonForms
      :data="modelValue ?? {}"
      :schema="schema as JsonSchema"
      :uischema="uischema"
      :renderers="nuxtUiRenderers"
      :readonly="readonly"
      validation-mode="NoValidation"
      :additional-errors="additionalErrors"
      @change="onChange"
    />
    <ul v-if="issues.some((issue) => issue.pointer === '')" class="text-xs text-error" role="alert">
      <li v-for="issue in issues.filter((entry) => entry.pointer === '')" :key="issue.message">{{ issue.message }}</li>
    </ul>
  </div>
</template>
