<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { Compartment, EditorState, type Extension } from '@codemirror/state'
import {
  EditorView,
  crosshairCursor,
  drawSelection,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
  placeholder as placeholderExtension,
  rectangularSelection,
} from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { bracketMatching, foldGutter, foldKeymap, indentOnInput } from '@codemirror/language'
import { closeBrackets, closeBracketsKeymap, completionKeymap } from '@codemirror/autocomplete'
import { highlightSelectionMatches, search, searchKeymap } from '@codemirror/search'
import { lintGutter, linter, type Diagnostic } from '@codemirror/lint'
import { json } from '@codemirror/lang-json'
import { yaml } from '@codemirror/lang-yaml'
import { editorHighlight, editorTheme } from './setup/theme'

/**
 * Text-level editor (spec section 9.1) on CodeMirror 6. `lint` returns diagnostics for the current text; extra
 * `extensions` (schema linting, binding decorations) are swapped through a compartment without recreating the view.
 */
const props = withDefaults(
  defineProps<{
    modelValue: string
    language: 'json' | 'yaml'
    readonly?: boolean
    height?: 'auto' | number
    placeholder?: string
    lint?: (text: string, view: EditorView) => Diagnostic[] | Promise<Diagnostic[]>
    extensions?: Extension[]
    ariaLabel?: string
  }>(),
  {
    readonly: false,
    height: 'auto',
    placeholder: undefined,
    lint: undefined,
    extensions: () => [],
    ariaLabel: 'Code editor',
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
  save: []
  ready: [view: EditorView]
}>()

const host = ref<HTMLDivElement>()
const view = shallowRef<EditorView>()

const languageSlot = new Compartment()
const editableSlot = new Compartment()
const lintSlot = new Compartment()
const extraSlot = new Compartment()

const languageExtension = () => (props.language === 'json' ? json() : yaml())
const editableExtension = () => [EditorState.readOnly.of(props.readonly), EditorView.editable.of(!props.readonly)]
const lintExtension = () =>
  props.lint ? [linter((target) => props.lint!(target.state.doc.toString(), target), { delay: 300 }), lintGutter()] : []

onMounted(() => {
  view.value = new EditorView({
    parent: host.value!,
    state: EditorState.create({
      doc: props.modelValue,
      extensions: [
        lineNumbers(),
        foldGutter(),
        highlightActiveLineGutter(),
        history(),
        drawSelection(),
        indentOnInput(),
        bracketMatching(),
        closeBrackets(),
        rectangularSelection(),
        crosshairCursor(),
        highlightActiveLine(),
        highlightSelectionMatches(),
        search({ top: true }),
        keymap.of([
          { key: 'Mod-s', preventDefault: true, run: () => (emit('save'), true) },
          ...closeBracketsKeymap,
          ...defaultKeymap,
          ...searchKeymap,
          ...historyKeymap,
          ...foldKeymap,
          ...completionKeymap,
          indentWithTab,
        ]),
        editorTheme,
        editorHighlight,
        EditorView.lineWrapping,
        EditorView.contentAttributes.of({ 'aria-label': props.ariaLabel }),
        props.placeholder ? placeholderExtension(props.placeholder) : [],
        languageSlot.of(languageExtension()),
        editableSlot.of(editableExtension()),
        lintSlot.of(lintExtension()),
        extraSlot.of(props.extensions),
        EditorView.updateListener.of((update) => {
          if (update.docChanged) {
            emit('update:modelValue', update.state.doc.toString())
          }
        }),
      ],
    }),
  })
  emit('ready', view.value)
})

onBeforeUnmount(() => view.value?.destroy())

// External changes (a format switch, a revert) replace the document; typing does not loop back.
watch(
  () => props.modelValue,
  (value) => {
    const current = view.value?.state.doc.toString()
    if (view.value && value !== current) {
      view.value.dispatch({ changes: { from: 0, to: current!.length, insert: value } })
    }
  },
)
watch(
  () => props.language,
  () => view.value?.dispatch({ effects: languageSlot.reconfigure(languageExtension()) }),
)
watch(
  () => props.readonly,
  () => view.value?.dispatch({ effects: editableSlot.reconfigure(editableExtension()) }),
)
watch(
  () => props.lint,
  () => view.value?.dispatch({ effects: lintSlot.reconfigure(lintExtension()) }),
)
watch(
  () => props.extensions,
  (value) => view.value?.dispatch({ effects: extraSlot.reconfigure(value) }),
)

defineExpose({
  view,
  focus: () => view.value?.focus(),
  /** Scrolls to a 1-based line and selects its start. */
  goToLine(line: number) {
    const target = view.value
    if (!target) {
      return
    }
    const position = target.state.doc.line(Math.min(Math.max(line, 1), target.state.doc.lines)).from
    target.dispatch({ selection: { anchor: position }, effects: EditorView.scrollIntoView(position, { y: 'center' }) })
    target.focus()
  },
})
</script>

<template>
  <div
    ref="host"
    class="code-editor overflow-hidden rounded-md border border-default"
    :class="height === 'auto' ? 'min-h-24' : ''"
    :style="height === 'auto' ? undefined : { height: `${height}px` }"
  />
</template>

<style scoped>
.code-editor :deep(.cm-editor) {
  height: 100%;
}

.code-editor :deep(.cm-scroller) {
  max-height: v-bind("height === 'auto' ? '70vh' : 'none'");
}
</style>
