<script setup lang="ts">
import { computed, defineAsyncComponent, ref, useId, useTemplateRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import { EditorView } from '@codemirror/view'
import { isNotFound } from '@42for.net/2splatform.sdk'
import { invalidateByPath, pathToQueryKey } from '@42for.net/2splatform.sdk/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { compositionLines, compositionSteps, stepSource, type CompositionStepView } from '@/utils/composition-lines'
import { metaOf } from '@/utils/annotation-key'
import { modelingPath } from '@/utils/modeling'
import {
  compositionDecorations,
  setSelectedStep,
  stepLines,
  type CompositionMarkerStep,
} from '@/components/editor/setup/composition'

// CodeMirror is loaded on demand, as in DocumentEditor.
const CodeEditor = defineAsyncComponent(() => import('@/components/editor/CodeEditor.vue'))

/**
 * The Composition view of the Effective tab (spec 8.5 of the Composition of the Effective Configuration): the merged
 * documents as ordered steps, and the effective document as JSON with the step that set each line in the gutter.
 * `effectiveHash` is the Hash of the Effective read, compared with the composition's to notice a stale view.
 */
const props = defineProps<{ annotationKey: string; effectiveHash?: string | null }>()

const route = useRoute()
const context = useContextStore()
const catalog = useCatalogStore()
const queryClient = useQueryClient()
const canEdit = useCan('edit')

const composition = useConfigurationComposition(() => props.annotationKey)
const missing = computed(() => composition.isError.value && isNotFound(composition.error.value))
const steps = computed(() => compositionSteps(composition.data.value))
const byOrder = computed(() => new Map(steps.value.map((step) => [step.order, step])))
const lines = computed(() => compositionLines(composition.data.value?.Content ?? {}, composition.data.value?.Origins))
const text = computed(() => lines.value.map((line) => line.text).join('\n'))
const empty = computed(() => Object.keys(composition.data.value?.Content ?? {}).length === 0)

const selected = ref<number | null>(null)
// The line the status names: the cursor's, a clicked marker's, or the first line of the step selected in the list.
const status = ref<{ line?: number; step?: number }>()
const editor = useTemplateRef<{ view?: EditorView }>('editor')

function title(step: CompositionStepView): string {
  const applied = `Applied by ${step.node}${step.mergedInto ? `, merged into ${step.mergedInto}` : ''}`
  return `Step ${step.order} · ${stepSource(step)}${step.version ? ` · v${step.version}` : ''}\n${applied}`
}

const extensions = computed(() => {
  const markers = new Map<number, CompositionMarkerStep>(
    steps.value.map((step) => [
      step.order,
      { order: step.order, code: step.code, template: step.kind === 'Template', title: title(step) },
    ]),
  )
  return [
    compositionDecorations({
      lines: lines.value.map((line) => line.step),
      steps: markers,
      onSelect: (order, line) => {
        selected.value = order
        status.value = { line }
        applySelection()
      },
      onCursor: (line) => (status.value = { line }),
    }),
  ]
})

/**
 * Highlights the lines of the selected step; from the list, it scrolls the first one into view, the focus stays. The
 * scroll goes only as far as the line needs: CodeMirror scrolls the panel around the editor too.
 */
function applySelection(view: EditorView | undefined = editor.value?.view, scroll = false) {
  if (!view) {
    return
  }
  const first = selected.value === null ? undefined : stepLines(view.state, selected.value)[0]
  view.dispatch({
    effects: [
      setSelectedStep.of(selected.value),
      ...(scroll && first ? [EditorView.scrollIntoView(view.state.doc.line(first).from, { y: 'nearest' })] : []),
    ],
  })
}

function toggle(order: number) {
  selected.value = selected.value === order ? null : order
  const first = lines.value.findIndex((line) => line.step === order) + 1
  status.value = selected.value === null ? undefined : first ? { line: first } : { step: order }
  applySelection(editor.value?.view, true)
}

// The route keeps the view when it opens another configuration.
watch(
  () => props.annotationKey,
  () => {
    selected.value = null
    status.value = undefined
    applySelection()
  },
)

const statusText = computed(() => {
  const line = status.value?.line
  const order = line ? lines.value[line - 1]?.step : status.value?.step
  const step = order === undefined ? undefined : byOrder.value.get(order)
  if (line) {
    return step ? `Line ${line}: step ${step.order} · ${stepSource(step)}` : `Line ${line}: the document itself`
  }
  return step ? `Step ${step.order} · ${stepSource(step)}: sets no value` : ''
})

const base = computed(() => `/${context.org}/${context.project}/${context.view}`)
const templates = computed(() =>
  canEdit.value && context.scope ? modelingPath(context.scope, 'templates') : undefined,
)

/** An ancestor's configuration opens its Edit tab while it is in the catalog; the composition includes deleted ones. */
function sourceLink(step: CompositionStepView) {
  if (step.kind === 'Template') {
    return templates.value && step.code ? `${templates.value}/${step.code}` : undefined
  }
  return !step.own && step.annotationKey && catalog.byKey.has(step.annotationKey)
    ? { path: `${base.value}/configurations/${step.annotationKey}`, query: { q: route.query.q } }
    : undefined
}

// Until the catalog of the view is loaded, an ancestor is neither linked nor marked as deleted.
const notInCatalog = (step: CompositionStepView) =>
  catalog.status === 'ready' &&
  step.kind === 'Configuration' &&
  !step.own &&
  !!step.annotationKey &&
  !catalog.byKey.has(step.annotationKey)

// The Effective query keeps its 30-second staleTime, so a different hash is often an older Effective read: it is
// read again once per composition, and only a difference that stays is reported. A composition being read again may
// still show an older hash, so the hashes are compared once it is read.
const hash = computed(() => composition.data.value?.Hash ?? undefined)
const differs = computed(
  () => !composition.isFetching.value && !!hash.value && !!props.effectiveHash && hash.value !== props.effectiveHash,
)
const configurationPath = computed(() => `/v1${base.value}/configuration/${props.annotationKey}`)
const rechecked = ref<string>()
let rechecking: string | undefined

/** A difference that appears again, or on another configuration, reads the Effective document again. */
function forgetRecheck() {
  rechecking = undefined
  rechecked.value = undefined
}

watch(() => props.annotationKey, forgetRecheck)

watch(
  [differs, hash, () => props.effectiveHash],
  async ([value, current, effectiveHash]) => {
    if (current && current === effectiveHash) {
      forgetRecheck()
    }
    if (!value || !current || rechecking === current) {
      return
    }
    const annotationKey = props.annotationKey
    rechecking = current
    await queryClient.refetchQueries({ queryKey: pathToQueryKey(configurationPath.value), exact: true })
    // the hashes agreed meanwhile, or another configuration is shown: its own recheck decides
    if (rechecking === current && props.annotationKey === annotationKey) {
      rechecked.value = current
    }
  },
  { immediate: true },
)

const stale = computed(() => differs.value && rechecked.value === hash.value)
const heading = useTemplateRef<HTMLElement>('heading')

/** Reads both documents again: the Effective read, the composition and everything else under the configuration. */
function refresh() {
  void invalidateByPath(queryClient, configurationPath.value)
  // the warning and its button are hidden while the composition is read again: the focus goes on to the steps
  heading.value?.focus()
}

const headingId = useId()
const indent = (step: CompositionStepView) => ({ paddingInlineStart: `${step.depth * 1.25}rem` })
const tint = (step: CompositionStepView) => metaOf(step.code)?.bgClass
</script>

<template>
  <div class="space-y-4">
    <LoadingState v-if="composition.isPending.value" />
    <EmptyState
      v-else-if="missing"
      icon="i-lucide-file-x"
      title="No configuration"
      description="Nothing is configured or inherited for this annotation yet."
    />
    <ErrorState v-else-if="composition.isError.value" :error="composition.error.value" @retry="composition.refetch()" />
    <template v-else>
      <p class="text-sm text-muted">
        Each line shows the step that set it. The ancestors are merged first, each with its own ancestors, template and
        content, then the type template, then the own content. An ancestor shared by two branches is applied once per
        branch, and a value that a later document repeats keeps the document that set it first.
      </p>

      <UAlert
        v-if="stale"
        color="warning"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Read at different times"
        description="The Effective document and the composition were read at different times, and a document changed in between. Refresh reads both again."
        :actions="[{ label: 'Refresh', color: 'warning', variant: 'outline', onClick: refresh }]"
      />
      <!-- the warning comes after the background read: a status rendered beforehand announces it -->
      <p class="sr-only" role="status">
        {{ stale ? 'The Effective document and the composition were read at different times.' : '' }}
      </p>

      <section class="ph-no-capture space-y-2" :aria-labelledby="headingId">
        <h3 :id="headingId" ref="heading" tabindex="-1" class="text-xs font-semibold text-muted uppercase outline-none">
          Steps, in the order applied
        </h3>
        <ol class="space-y-2">
          <li
            v-for="step in steps"
            :key="step.order"
            class="flex items-start gap-2"
            :style="indent(step)"
            :data-step="step.order"
          >
            <UButton
              :label="String(step.order)"
              size="xs"
              :color="selected === step.order ? 'primary' : 'neutral'"
              variant="outline"
              class="min-w-8 justify-center font-mono tabular-nums text-highlighted"
              :class="[tint(step), selected === step.order ? 'font-semibold ring-2' : '']"
              :aria-pressed="selected === step.order"
              :aria-label="`Show the lines of step ${step.order}`"
              @click="toggle(step.order)"
            />
            <div class="min-w-0 flex-1 space-y-0.5">
              <div class="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
                <AnnotationTypeBadge :type="step.code" variant="icon" />
                <UIcon
                  v-if="step.kind === 'Template'"
                  name="i-lucide-layout-template"
                  class="size-3.5 text-muted"
                  aria-hidden="true"
                />
                <RouterLink
                  v-if="sourceLink(step)"
                  :to="sourceLink(step)!"
                  class="font-mono text-xs break-all text-primary hover:underline"
                >
                  {{ stepSource(step) }}
                </RouterLink>
                <span
                  v-else-if="notInCatalog(step)"
                  class="font-mono text-xs break-all text-muted line-through"
                  title="Not in the catalog: the annotation is deleted"
                >
                  {{ stepSource(step) }}
                </span>
                <span
                  v-else
                  class="font-mono text-xs break-all"
                  :class="{ 'font-semibold text-highlighted': step.own }"
                >
                  {{ stepSource(step) }}
                </span>
                <span v-if="step.via" class="text-xs text-muted">
                  via <span class="font-mono break-all">{{ step.via }}</span>
                </span>
                <span v-if="step.version" class="font-mono text-xs text-muted">v{{ step.version }}</span>
                <UBadge v-if="notInCatalog(step)" label="Deleted" color="neutral" variant="outline" size="sm" />
                <UBadge
                  v-if="step.presence === 'Missing'"
                  label="No configuration"
                  color="neutral"
                  variant="outline"
                  size="sm"
                />
                <UBadge
                  v-else-if="step.presence === 'Empty'"
                  label="Empty"
                  color="neutral"
                  variant="outline"
                  size="sm"
                />
                <UBadge
                  v-if="step.again"
                  label="Applied again"
                  icon="i-lucide-repeat"
                  color="neutral"
                  variant="subtle"
                  size="sm"
                />
              </div>
              <details v-if="step.paths.length" class="text-xs">
                <summary class="cursor-pointer text-muted">
                  {{ step.paths.length }} {{ step.paths.length === 1 ? 'value' : 'values' }}
                </summary>
                <ul class="mt-1 space-y-0.5 ps-4 font-mono break-all">
                  <li v-for="path in step.paths" :key="path">{{ path }}</li>
                </ul>
              </details>
              <p v-else class="text-xs text-muted">No values</p>
            </div>
          </li>
        </ol>
      </section>

      <p v-if="empty" class="text-sm text-muted">The effective document is empty: no step sets a value.</p>
      <CodeEditor
        v-else
        ref="editor"
        :model-value="text"
        language="json"
        readonly
        :extensions="extensions"
        aria-label="Effective configuration, by step"
        @ready="(view: EditorView) => applySelection(view)"
      />
      <!-- also for an empty document: a toggle there names a step that sets no value -->
      <p class="ph-no-capture min-h-4 text-xs text-muted" aria-live="polite">{{ statusText }}</p>
    </template>
  </div>
</template>
