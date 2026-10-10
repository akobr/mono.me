<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import type { Diagnostic } from '@codemirror/lint'
import type { EditorView } from '@codemirror/view'
import { parseDocument, serialize, type DocumentFormat, type ParseResult } from '@/utils/json-yaml'

// CodeMirror is loaded on demand, so routes without an editor do not ship it.
const CodeEditor = defineAsyncComponent(() => import('./CodeEditor.vue'))

export interface DocumentIssue {
  path?: string
  message: string
}

/**
 * Value-level editor (spec section 9.1/9.2). It keeps a text buffer, emits the parsed value whenever the text
 * parses, and keeps the last valid value otherwise. A format switch serializes the last valid value and is
 * refused while the buffer has a syntax error.
 */
const props = withDefaults(
  defineProps<{
    modelValue: unknown
    format: DocumentFormat
    readonly?: boolean
    height?: 'auto' | number
    /** Semantic validation of a parsed value (shown under the editor). */
    validate?: (value: unknown) => DocumentIssue[]
    /** Extra CodeMirror extensions (schema linting, binding decorations). */
    extensions?: import('@codemirror/state').Extension[]
    /** Hide the JSON | YAML switch (the parent shows its own). */
    hideToggle?: boolean
    ariaLabel?: string
  }>(),
  {
    readonly: false,
    height: 'auto',
    validate: undefined,
    extensions: () => [],
    hideToggle: false,
    ariaLabel: 'Document editor',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: unknown]
  'update:format': [format: DocumentFormat]
  'update:valid': [valid: boolean]
  save: []
}>()

const text = ref(serialize(props.modelValue, props.format))
const result = ref<ParseResult>(parseDocument(text.value, props.format))
const blocked = ref<string>()
let lastValid: unknown = props.modelValue
let lastEmitted = JSON.stringify(props.modelValue ?? null)

const issues = computed<DocumentIssue[]>(() =>
  result.value.ok && props.validate ? props.validate(result.value.value) : [],
)
const valid = computed(() => result.value.ok && issues.value.length === 0)

watch(valid, (value) => emit('update:valid', value), { immediate: true })

function onText(value: string) {
  text.value = value
  result.value = parseDocument(value, props.format)
  blocked.value = undefined

  if (result.value.ok) {
    lastValid = result.value.value
    lastEmitted = JSON.stringify(lastValid ?? null)
    emit('update:modelValue', lastValid)
  }
}

// An external value change (revert, reload) re-serializes the buffer.
watch(
  () => props.modelValue,
  (value) => {
    const incoming = JSON.stringify(value ?? null)
    if (incoming !== lastEmitted) {
      lastValid = value
      lastEmitted = incoming
      text.value = serialize(value, props.format)
      result.value = parseDocument(text.value, props.format)
    }
  },
  { deep: true },
)

/** Switches the format, unless the buffer has a syntax error. */
function requestFormat(format: DocumentFormat): boolean {
  if (format === props.format) {
    return true
  }

  if (!result.value.ok) {
    blocked.value = `Fix the syntax error${result.value.line ? ` at line ${result.value.line}` : ''} first.`
    return false
  }

  emit('update:format', format)
  return true
}

// A format changed by the parent (⌘⇧F, preferences) re-serializes the last valid value.
watch(
  () => props.format,
  (format, previous) => {
    if (!result.value.ok) {
      blocked.value = `Fix the syntax error${result.value.line ? ` at line ${result.value.line}` : ''} first.`
      emit('update:format', previous)
      return
    }
    text.value = serialize(lastValid, format)
    result.value = parseDocument(text.value, format)
  },
)

const lint = (source: string, view: EditorView): Diagnostic[] => {
  const parsed = parseDocument(source, props.format)
  if (parsed.ok) {
    return []
  }
  const line = view.state.doc.line(Math.min(Math.max(parsed.line ?? 1, 1), view.state.doc.lines))
  const from = Math.min(line.from + Math.max((parsed.column ?? 1) - 1, 0), line.to)
  return [{ from, to: Math.min(from + 1, view.state.doc.length), severity: 'error', message: parsed.message }]
}

defineExpose({ requestFormat, valid })
</script>

<template>
  <div class="flex min-h-0 flex-col gap-2">
    <div v-if="!hideToggle || $slots.actions" class="flex flex-wrap items-center gap-2">
      <FormatToggle
        v-if="!hideToggle"
        :model-value="format"
        @update:model-value="(mode) => mode !== 'form' && requestFormat(mode)"
      />
      <UTooltip v-if="format === 'yaml'" text="Comments are not kept: the API stores JSON.">
        <UIcon name="i-lucide-info" class="size-4 text-dimmed" />
      </UTooltip>
      <div class="ms-auto flex items-center gap-2">
        <slot name="actions" />
      </div>
    </div>

    <CodeEditor
      :model-value="text"
      :language="format"
      :readonly="readonly"
      :height="height"
      :lint="lint"
      :extensions="extensions"
      :aria-label="ariaLabel"
      @update:model-value="onText"
      @save="emit('save')"
    />

    <UAlert
      v-if="blocked"
      color="warning"
      variant="subtle"
      icon="i-lucide-triangle-alert"
      :description="blocked"
      :close="true"
      @update:open="blocked = undefined"
    />
    <p v-if="!result.ok" class="text-xs text-error" role="alert">
      {{ result.line ? `Line ${result.line}: ` : '' }}{{ result.message }}
    </p>
    <p v-else-if="result.warning" class="text-xs text-warning">{{ result.warning }}</p>
    <ul v-if="issues.length" class="space-y-0.5 text-xs text-error" role="alert">
      <li v-for="issue in issues" :key="`${issue.path}:${issue.message}`">
        <code v-if="issue.path" class="font-mono">{{ issue.path }}</code>
        {{ issue.message }}
      </li>
    </ul>
  </div>
</template>
