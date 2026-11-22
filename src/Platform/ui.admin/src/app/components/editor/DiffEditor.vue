<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { EditorState, RangeSetBuilder } from '@codemirror/state'
import { Decoration, EditorView, lineNumbers } from '@codemirror/view'
import { json } from '@codemirror/lang-json'
import { MergeView, unifiedMergeView } from '@codemirror/merge'
import type { DiffHunk, DiffStats } from '@42for.net/2splatform.sdk'
import { editorHighlight, editorTheme } from './setup/theme'

/**
 * JSON diff (spec sections 9.1 and 11.3): CodeMirror MergeView side by side at lg and above, the unified merge
 * view below. Unchanged regions collapse with the server's hunk rules (3 lines of context, 6 lines minimum).
 * The server DiffResult supplies the counts and the jump-to-change positions.
 *
 * `embedded` is the editor as one document of a list (spec "Copy and Compare Annotations and Configurations Between
 * Views", section 6): the page has one set of controls and one row of sides for all its documents, and it scrolls,
 * so the editor has no toolbar, no side labels and no height limit, and takes its layout from its props.
 */
const props = withDefaults(
  defineProps<{
    original: string
    modified: string
    stats?: DiffStats
    hunks?: DiffHunk[]
    layout?: 'auto' | 'split' | 'unified'
    collapseUnchanged?: boolean
    originalLabel?: string
    modifiedLabel?: string
    embedded?: boolean
  }>(),
  {
    stats: undefined,
    hunks: () => [],
    layout: 'auto',
    collapseUnchanged: true,
    originalLabel: 'Before',
    modifiedLabel: 'After',
    embedded: false,
  },
)

const host = ref<HTMLDivElement>()
const merge = shallowRef<MergeView>()
const unified = shallowRef<EditorView>()
const wide = useMediaQuery('(min-width: 1024px)')
const collapse = ref(props.collapseUnchanged)
const chosenLayout = ref<'auto' | 'split' | 'unified'>(props.layout)
const current = ref(-1)

// The toolbar changes the local state. A caller that changes a prop, as a list does for all its documents, wins.
watch(
  () => props.layout,
  (value) => (chosenLayout.value = value),
)
watch(
  () => props.collapseUnchanged,
  (value) => (collapse.value = value),
)

const effectiveLayout = computed(() =>
  chosenLayout.value === 'auto' ? (wide.value ? 'split' : 'unified') : chosenLayout.value,
)

const changes = computed(() => props.hunks.length)
const summary = computed(() => {
  const stats = props.stats
  if (!stats) {
    return undefined
  }
  return `+${stats.Additions ?? 0} −${stats.Deletions ?? 0} · ${changes.value} ${changes.value === 1 ? 'change' : 'changes'}`
})

const readOnly = [EditorState.readOnly.of(true), EditorView.editable.of(false)]
// Every editor is a textbox, and a page can hold many of them: each one says which document it shows.
const base = (label: string) => [
  lineNumbers(),
  json(),
  editorTheme,
  editorHighlight,
  EditorView.lineWrapping,
  EditorView.contentAttributes.of({ 'aria-label': label }),
  ...readOnly,
]
const collapseOption = () => (collapse.value ? { margin: 3, minSize: 6 } : undefined)

/** In a list: the one side that has a document, when the other side has none. */
const oneSided = computed(() => {
  if (!props.embedded || (props.original === '') === (props.modified === '')) {
    return undefined
  }
  return props.original === '' ? ('modified' as const) : ('original' as const)
})

/** Every line of the document as an added or as a removed line. */
function wholeDocument(className: string) {
  return EditorView.decorations.of((view) => {
    const builder = new RangeSetBuilder<Decoration>()
    const line = Decoration.line({ class: className })
    for (let number = 1; number <= view.state.doc.lines; number++) {
      const start = view.state.doc.line(number).from
      builder.add(start, start, line)
    }
    return builder.finish()
  })
}

function destroy() {
  merge.value?.destroy()
  unified.value?.destroy()
  merge.value = undefined
  unified.value = undefined
}

function build() {
  destroy()
  if (!host.value) {
    return
  }

  if (effectiveLayout.value === 'split') {
    merge.value = new MergeView({
      parent: host.value,
      a: { doc: props.original, extensions: base(props.originalLabel) },
      b: { doc: props.modified, extensions: base(props.modifiedLabel) },
      collapseUnchanged: collapseOption(),
      gutter: true,
      highlightChanges: true,
    })
  } else if (oneSided.value) {
    // In a list a side without a document is an empty text. The unified view would draw its one empty line as a
    // removed line above a new document, so the one document there is, is drawn whole as added or as removed.
    const added = oneSided.value === 'modified'
    unified.value = new EditorView({
      parent: host.value,
      state: EditorState.create({
        doc: added ? props.modified : props.original,
        extensions: [
          ...base(added ? props.modifiedLabel : props.originalLabel),
          wholeDocument(added ? 'cm-insertedLine' : 'cm-deletedLine'),
        ],
      }),
    })
  } else {
    unified.value = new EditorView({
      parent: host.value,
      state: EditorState.create({
        doc: props.modified,
        extensions: [
          ...base(`${props.modifiedLabel}, compared with ${props.originalLabel}`),
          unifiedMergeView({
            original: props.original,
            mergeControls: false,
            collapseUnchanged: collapseOption(),
            highlightChanges: true,
          }),
        ],
      }),
    })
  }
}

onMounted(build)
onBeforeUnmount(destroy)
watch(
  [
    () => props.original,
    () => props.modified,
    () => props.originalLabel,
    () => props.modifiedLabel,
    effectiveLayout,
    collapse,
  ],
  build,
)

/** Scrolls the modified side to the hunk's first new line. */
function jump(index: number) {
  const hunk = props.hunks[index]
  const target = merge.value?.b ?? unified.value
  if (!hunk || !target) {
    return
  }
  current.value = index
  const line = target.state.doc.line(Math.min(Math.max(hunk.NewStart ?? 1, 1), target.state.doc.lines))
  target.dispatch({ effects: EditorView.scrollIntoView(line.from, { y: 'center' }) })
}

const next = () => jump(Math.min(current.value + 1, changes.value - 1))
const previous = () => jump(Math.max(current.value - 1, 0))

defineExpose({ jump })
</script>

<template>
  <div class="ph-no-capture flex min-h-0 flex-col gap-2">
    <div v-if="!embedded" class="flex flex-wrap items-center gap-2 text-sm">
      <span v-if="summary" class="font-mono text-xs">
        <span class="text-success">+{{ stats?.Additions ?? 0 }}</span>
        <span class="text-error"> −{{ stats?.Deletions ?? 0 }}</span>
        <span class="text-muted"> · {{ changes }} {{ changes === 1 ? 'change' : 'changes' }}</span>
      </span>
      <UFieldGroup v-if="changes" size="xs">
        <UButton
          icon="i-lucide-chevron-up"
          color="neutral"
          variant="outline"
          aria-label="Previous change"
          :disabled="current <= 0"
          @click="previous"
        />
        <UButton
          icon="i-lucide-chevron-down"
          color="neutral"
          variant="outline"
          aria-label="Next change"
          :disabled="current >= changes - 1"
          @click="next"
        />
      </UFieldGroup>
      <USwitch v-model="collapse" size="xs" label="Collapse unchanged" />
      <UTabs
        v-model="chosenLayout"
        :items="[
          { label: 'Auto', value: 'auto' },
          { label: 'Split', value: 'split' },
          { label: 'Unified', value: 'unified' },
        ]"
        :content="false"
        size="xs"
        class="ms-auto"
        aria-label="Diff layout"
      />
    </div>
    <div v-if="effectiveLayout === 'split' && !embedded" class="grid grid-cols-2 text-xs text-muted">
      <span>{{ originalLabel }}</span>
      <span class="ps-2">{{ modifiedLabel }}</span>
    </div>
    <div
      ref="host"
      class="diff-editor min-h-0 overflow-auto"
      :class="embedded ? 'diff-editor-embedded' : 'rounded-md border border-default'"
    />
  </div>
</template>

<style scoped>
/* Embedded in a list, a document is as high as its lines, and the page scrolls. */
.diff-editor:not(.diff-editor-embedded) :deep(.cm-mergeView),
.diff-editor:not(.diff-editor-embedded) :deep(.cm-editor) {
  max-height: 70vh;
}

.diff-editor :deep(.cm-changedLine),
.diff-editor :deep(.cm-insertedLine) {
  background-color: color-mix(in oklab, var(--ui-success) 12%, transparent);
}

.diff-editor :deep(.cm-deletedChunk),
.diff-editor :deep(.cm-deletedLine) {
  background-color: color-mix(in oklab, var(--ui-error) 12%, transparent);
}

/* In a list the left side is the old one: its changed lines are what only that side has, the red of the page's "−". */
.diff-editor-embedded :deep(.cm-merge-a .cm-changedLine) {
  background-color: color-mix(in oklab, var(--ui-error) 12%, transparent);
}
</style>
