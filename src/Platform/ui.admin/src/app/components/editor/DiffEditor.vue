<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { EditorState } from '@codemirror/state'
import { EditorView, lineNumbers } from '@codemirror/view'
import { json } from '@codemirror/lang-json'
import { MergeView, unifiedMergeView } from '@codemirror/merge'
import type { DiffHunk, DiffStats } from '@42for.net/2splatform.sdk'
import { editorHighlight, editorTheme } from './setup/theme'

/**
 * JSON diff (spec sections 9.1 and 11.3): CodeMirror MergeView side by side at lg and above, the unified merge
 * view below. Unchanged regions collapse with the server's hunk rules (3 lines of context, 6 lines minimum).
 * The server DiffResult supplies the counts and the jump-to-change positions.
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
  }>(),
  {
    stats: undefined,
    hunks: () => [],
    layout: 'auto',
    collapseUnchanged: true,
    originalLabel: 'Before',
    modifiedLabel: 'After',
  },
)

const host = ref<HTMLDivElement>()
const merge = shallowRef<MergeView>()
const unified = shallowRef<EditorView>()
const wide = useMediaQuery('(min-width: 1024px)')
const collapse = ref(props.collapseUnchanged)
const chosenLayout = ref<'auto' | 'split' | 'unified'>(props.layout)
const current = ref(-1)

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
const base = () => [lineNumbers(), json(), editorTheme, editorHighlight, EditorView.lineWrapping, ...readOnly]
const collapseOption = () => (collapse.value ? { margin: 3, minSize: 6 } : undefined)

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
      a: { doc: props.original, extensions: base() },
      b: { doc: props.modified, extensions: base() },
      collapseUnchanged: collapseOption(),
      gutter: true,
      highlightChanges: true,
    })
  } else {
    unified.value = new EditorView({
      parent: host.value,
      state: EditorState.create({
        doc: props.modified,
        extensions: [
          ...base(),
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
watch([() => props.original, () => props.modified, effectiveLayout, collapse], build)

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
    <div class="flex flex-wrap items-center gap-2 text-sm">
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
    <div v-if="effectiveLayout === 'split'" class="grid grid-cols-2 text-xs text-muted">
      <span>{{ originalLabel }}</span>
      <span class="ps-2">{{ modifiedLabel }}</span>
    </div>
    <div ref="host" class="diff-editor min-h-0 overflow-auto rounded-md border border-default" />
  </div>
</template>

<style scoped>
.diff-editor :deep(.cm-mergeView),
.diff-editor :deep(.cm-editor) {
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
</style>
