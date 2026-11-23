<script setup lang="ts">
import { computed, defineAsyncComponent, ref, useId, useTemplateRef } from 'vue'
import { useIntersectionObserver } from '@vueuse/core'
import { stateOf, type CompareFile } from '@/composables/useViewCompare'
import type { Scope } from '@/stores/context'
import { codeOfKey } from '@/utils/annotation-key'
import { modelingPath } from '@/utils/modeling'
import { compareLabel, type CompareFilter } from '@/utils/view-compare'
import type { ConfigurationContent, ViewItemKind } from '@/utils/view-documents'

const DiffEditor = defineAsyncComponent(() => import('@/components/editor/DiffEditor.vue'))

/**
 * One key of the comparison of a selection across two views, as a file of "Files changed" (spec "Copy and Compare
 * Annotations and Configurations Between Views", section 5): a heading that opens and closes the section, with the
 * state of the key and its changed lines, the copy of this one key, links to the documents, what a side lacks, and
 * the diff. The target is the old side of the diff and the source the new one, as a pull request from the source into
 * the target.
 */
const props = defineProps<{
  file: CompareFile
  kind: ViewItemKind
  content: ConfigurationContent
  source: Scope
  target: string
  layout: 'auto' | 'split' | 'unified'
  open: boolean
  /** A copy from the source would change the target, and the target can take one. */
  copyable?: boolean
  /** The element that scrolls the page; the viewport without one. */
  scrollRoot?: HTMLElement | null
}>()
const emit = defineEmits<{ toggle: []; copy: [] }>()

const bodyId = useId()

// Literal names: the client bundle holds only the icons its scan finds in the sources. The label carries the state,
// the color and the icon only repeat it.
const states: Record<
  Exclude<CompareFilter, 'all'>,
  { color: 'success' | 'info' | 'neutral' | 'warning' | 'error'; icon: string }
> = {
  different: { color: 'warning', icon: 'i-lucide-diff' },
  identical: { color: 'neutral', icon: 'i-lucide-equal' },
  onlySource: { color: 'success', icon: 'i-lucide-plus' },
  onlyTarget: { color: 'neutral', icon: 'i-lucide-minus' },
  absent: { color: 'neutral', icon: 'i-lucide-circle-dashed' },
  deleted: { color: 'error', icon: 'i-lucide-trash-2' },
  effectiveDiffers: { color: 'info', icon: 'i-lucide-layers' },
  failed: { color: 'error', icon: 'i-lucide-triangle-alert' },
}

const names = computed(() => ({
  source: props.source.view,
  target: props.target,
  own: props.kind === 'configurations' && props.content === 'own',
}))

const state = computed(() => stateOf(props.file))
const badge = computed(() =>
  state.value === 'loading' || state.value === 'all'
    ? undefined
    : { ...states[state.value], label: compareLabel(state.value, names.value) },
)

const result = computed(() => props.file.shown)
/** The sides that have the shown document. */
const inSource = computed(() => ['different', 'identical', 'onlySource'].includes(result.value?.status ?? ''))
const inTarget = computed(() => ['different', 'identical', 'onlyTarget'].includes(result.value?.status ?? ''))
// Without a document on either side there is nothing to open. A failed key shows its error, a loading one its line.
const expandable = computed(() => inSource.value || inTarget.value)

const lines = computed(() => {
  const { additions = 0, deletions = 0 } = result.value ?? {}
  return additions || deletions
    ? {
        additions,
        deletions,
        label: `${additions} ${additions === 1 ? 'line' : 'lines'} added, ${deletions} removed`,
      }
    : undefined
})

/** The annotation or the configuration detail of the key in a view. */
const detail = (view: string) =>
  `/${props.source.org}/${props.source.project}/${view}/${props.kind}/${encodeURIComponent(props.file.key)}`
const deletedPath = computed(() => modelingPath({ ...props.source, view: props.target }, 'deleted'))

// An editor is built when its open section first comes near the visible part of the page, and it stays from then
// on. So a hundred documents do not build two hundred editors at once.
const root = useTemplateRef<HTMLElement>('root')
const near = ref(false)
useIntersectionObserver(
  () => (props.open && expandable.value && !near.value ? root.value : undefined),
  (entries) => {
    if (entries.some((entry) => entry.isIntersecting)) {
      near.value = true
    }
  },
  { root: () => props.scrollRoot, rootMargin: '400px 0px' },
)
</script>

<template>
  <!-- No landmark per key: a hundred regions would bury the ones of the page, and the headings lead to every key. -->
  <section ref="root" class="overflow-hidden rounded-md border border-default" :data-key="file.key">
    <div class="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2">
      <h3 class="min-w-0 flex-1">
        <component
          :is="expandable ? 'button' : 'span'"
          :type="expandable ? 'button' : undefined"
          class="flex w-full min-w-0 flex-wrap items-center gap-x-2 gap-y-1 rounded-sm text-left focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          :aria-expanded="expandable ? open : undefined"
          :aria-controls="expandable && open ? bodyId : undefined"
          @click="expandable && emit('toggle')"
        >
          <UIcon
            v-if="expandable"
            :name="open ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'"
            class="size-4 shrink-0 text-muted"
            aria-hidden="true"
          />
          <span v-else class="size-4 shrink-0" aria-hidden="true" />
          <AnnotationTypeBadge :type="codeOfKey(file.key)" variant="label" />
          <KeyText :value="file.key" size="sm" />
          <template v-if="state === 'loading'">
            <span class="h-4 w-32 animate-pulse rounded-sm bg-elevated" aria-hidden="true" />
            <span class="sr-only">Loading…</span>
          </template>
          <UBadge
            v-else-if="badge"
            :label="badge.label"
            :icon="badge.icon"
            :color="badge.color"
            variant="subtle"
            size="sm"
          />
          <UBadge
            v-if="file.effectiveDiffers"
            :label="compareLabel('effectiveDiffers', names)"
            :icon="states.effectiveDiffers.icon"
            :color="states.effectiveDiffers.color"
            variant="subtle"
            size="sm"
          />
          <span v-if="lines" class="font-mono text-xs whitespace-nowrap" role="img" :aria-label="lines.label">
            <span class="text-success">+{{ lines.additions }}</span>
            <span class="text-error"> −{{ lines.deletions }}</span>
          </span>
        </component>
      </h3>
      <div v-if="inSource || inTarget || file.deleted || copyable" class="flex flex-wrap items-center gap-1">
        <UButton
          v-if="copyable"
          :label="`Copy to ${target}`"
          icon="i-lucide-copy-plus"
          color="neutral"
          variant="outline"
          size="xs"
          @click="emit('copy')"
        />
        <UButton
          v-if="inSource"
          :label="`Open in ${source.view}`"
          :to="detail(source.view)"
          color="neutral"
          variant="ghost"
          size="xs"
        />
        <UButton
          v-if="inTarget"
          :label="`Open in ${target}`"
          :to="detail(target)"
          color="neutral"
          variant="ghost"
          size="xs"
        />
        <!-- A deleted key is restored on the Deleted page of the target view. -->
        <UButton
          v-if="file.deleted"
          label="Deleted"
          icon="i-lucide-archive-restore"
          :to="deletedPath"
          color="neutral"
          variant="ghost"
          size="xs"
        />
      </div>
    </div>

    <ul v-if="file.notes.length" class="space-y-1 px-3 pb-2 text-sm text-muted">
      <li v-for="note in file.notes" :key="note" class="flex items-start gap-1.5">
        <UIcon name="i-lucide-info" class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>{{ note }}</span>
      </li>
    </ul>

    <div v-if="file.error !== undefined" class="border-t border-default p-3">
      <ErrorState :error="file.error" compact @retry="file.retry()" />
    </div>
    <div v-else-if="expandable && open" :id="bodyId" class="border-t border-default">
      <DiffEditor
        v-if="near"
        embedded
        :original="result!.target"
        :modified="result!.source"
        :original-label="`${file.key} in ${target}`"
        :modified-label="`${file.key} in ${source.view}`"
        :layout="layout"
      />
      <!-- The room of an editor that is not built yet, so the sections below it are not all near the visible part. -->
      <div v-else class="h-24 animate-pulse bg-elevated/50" aria-hidden="true" />
    </div>
  </section>
</template>
