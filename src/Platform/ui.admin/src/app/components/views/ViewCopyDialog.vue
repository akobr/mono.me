<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, useId, useTemplateRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useEventListener } from '@vueuse/core'
import { useContextStore } from '@/stores/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { errorMessage } from '@/utils/errors'
import { documentKinds, outcomeLabel, outcomes } from '@/utils/transfers'
import { compareLocation, fitsInAddress, viewTransferLimit } from '@/utils/view-compare'
import { copyCounts, copySummary, copyWrites, type ViewCopyItem, type ViewCopyResult } from '@/utils/view-copy'
import { countOf, type ViewItemKind } from '@/utils/view-documents'
import { defaultViewBadge, isDefaultView } from '@/utils/views'

/**
 * Copies the selected annotations or configurations of this view (the source) to another view of the project (spec
 * "Copy and Compare Annotations and Configurations Between Views", section 7). Check runs the copy without writing
 * and shows what it would do; the production view takes a copy only after a check. While a copy runs the dialog
 * stays, and Stop ends it after the key that is under way. The copy takes what is stored, not unsaved edits.
 */
const props = withDefaults(
  defineProps<{
    kind: ViewItemKind
    keys: readonly string[]
    /** The target of the comparison the dialog was opened from; it is then not for choosing. */
    target?: string
    /** What that comparison saw in the target, by key: a key whose target changed since is skipped. */
    expected?: Readonly<Record<string, string | number | null>>
    /** Offer the comparison of the keys after the copy; the comparison page itself does not need it. */
    compare?: boolean
  }>(),
  { target: undefined, expected: undefined, compare: true },
)
const emit = defineEmits<{
  copied: [result: ViewCopyResult]
  /** The dialog is gone from the screen: the place that opened it gives the focus a home. */
  closed: []
}>()
const open = defineModel<boolean>('open', { default: false })

const context = useContextStore()
const router = useRouter()
const store = useViewTransferStore()
const others = useOtherViews()
const { progress, run } = useViewCopy()

const chosen = ref<string>()
const existing = ref<'overwrite' | 'skip'>('overwrite')
const missingAnnotations = ref(true)
/** What the last check found, for the target and the options as they are now. */
const plan = shallowRef<ViewCopyResult>()
/** The copy that ran; the dialog then shows only its result. */
const result = shallowRef<ViewCopyResult>()
/** Why a check ended as a whole. */
const failure = shallowRef<unknown>()
const running = ref<'check' | 'copy'>()
let controller: AbortController | undefined
/** Counts the changes of what a check holds for: a check that ran across one is no plan. */
let revision = 0
/** Counts the openings: a copy that a closed opening started does not show its result in the next one. */
let opening = 0
/** The selection of the copy that ran, for its comparison: the selection behind the dialog may change meanwhile. */
let copiedSelection: { kind: ViewItemKind; keys: string[] } | undefined

const number = new Intl.NumberFormat()
const statusId = useId()
const hintId = useId()
const banner = useTemplateRef<HTMLElement>('banner')

// A copy goes only to a view that the project lists: a write to any other name would create that view.
const fixed = computed(() => (others.has(props.target) ? props.target : undefined))
// A dialog opened for a comparison copies to the compared view or not at all: what the comparison saw of the target
// (`expected`) holds for no other view.
const destination = computed(() =>
  props.target !== undefined ? fixed.value : others.has(chosen.value) ? chosen.value : undefined,
)
/** The compared view is not a view of the project (any more). */
const lostTarget = computed(() => props.target !== undefined && others.known.value && !fixed.value)
const production = computed(() => isDefaultView(destination.value))
const there = computed(() => destination.value ?? 'the other view')
const tooMany = computed(() => props.keys.length > viewTransferLimit)

/** The only other view, or the one last used in the project. Memory never preselects the production view. */
function preselect(): void {
  const names = others.names.value
  const last = context.projectKey ? store.lastTarget(context.projectKey) : undefined
  chosen.value = names.length === 1 ? names[0] : names.find((name) => name === last && !isDefaultView(name))
}

/** Ends a run after the key that is under way. */
function stop(): void {
  controller?.abort()
}

watch(
  open,
  (isOpen) => {
    if (isOpen) {
      opening++
      revision++
      plan.value = undefined
      result.value = undefined
      failure.value = undefined
      existing.value = 'overwrite'
      missingAnnotations.value = true
      preselect()
    } else {
      // The dialog went while a run was going: nobody watches it any more.
      stop()
    }
  },
  { immediate: true },
)
// The views may arrive after the dialog opened.
watch(others.names, () => {
  if (open.value && !chosen.value) {
    preselect()
  }
})

// A check holds for what it checked: the source, the target, the options and the keys.
watch(
  [() => context.scope, () => props.kind, destination, existing, missingAnnotations, () => props.keys.join('\n')],
  () => {
    revision++
    plan.value = undefined
    failure.value = undefined
  },
)

// The view of the route changed under the dialog (the browser's Back, a link of the result): a detail keeps its
// instance, and the dialog with it. Its plan, its result and its links were those of another source, so it goes.
watch(
  () => context.scope,
  () => {
    open.value = false
  },
)

const title = computed(() => {
  const what = props.keys.length === 1 ? props.keys[0]! : countOf(props.kind, props.keys.length)
  return `Copy ${what} to ${fixed.value ?? 'another view'}`
})

const existingItems = computed(() => [
  {
    label: 'Overwrite',
    value: 'overwrite',
    description: `Replace its content with the content of ${context.view}.`,
  },
  { label: 'Skip', value: 'skip', description: 'Leave it as it is.' },
])

const copied = computed(() =>
  props.kind === 'annotations'
    ? `The title, description, documentation link, labels, values, the enabled or disabled state and the validity. Ancestors that ${there.value} lacks are copied too. Configurations are not copied.`
    : `The own configuration, as it is stored. Inherited values, templates and schemas stay those of ${there.value}.`,
)

/** The documents the checked copy would write; undefined before a check. */
const writes = computed(() => (plan.value ? copyWrites(plan.value.items) : undefined))
const ready = computed(() => !!context.scope && !!destination.value && props.keys.length > 0 && !tooMany.value)
// The production view takes a copy only after a check showed what it would change.
const needsCheck = computed(() => production.value && !plan.value)
const canCopy = computed(() => ready.value && !needsCheck.value && writes.value !== 0)
const copyLabel = computed(() => {
  if (writes.value === undefined) {
    return 'Copy'
  }
  return writes.value === 0 ? 'Nothing to copy' : `Copy ${number.format(writes.value)} to ${destination.value}`
})

const status = computed(() => {
  if (!running.value || !progress.value) {
    return ''
  }
  const { done, total } = progress.value
  return `${running.value === 'copy' ? 'Copied' : 'Checked'} ${number.format(done)} of ${number.format(total)}…`
})

async function start(dryRun: boolean): Promise<void> {
  const source = context.scope
  const to = destination.value
  if (!source || !to || running.value) {
    return
  }

  failure.value = undefined
  running.value = dryRun ? 'check' : 'copy'
  controller = new AbortController()
  if (context.projectKey) {
    store.rememberTarget(context.projectKey, to)
  }
  const selection = { kind: props.kind, keys: [...props.keys] }
  const checked = revision
  const openedAs = opening

  try {
    const outcome = await run(
      {
        source,
        target: to,
        ...selection,
        existing: existing.value,
        missingAnnotations: missingAnnotations.value,
        expected: props.expected,
        // A copy after a check writes only what the check showed: a source changed since is skipped.
        checked: dryRun ? undefined : plan.value?.sources,
        dryRun,
      },
      controller.signal,
    )

    if (dryRun) {
      // A check that was stopped, or that ended as a whole, is no plan to copy by. Neither is one that ran while the
      // keys, the target or an option changed: Copy would say what another copy writes.
      if (checked === revision) {
        failure.value = outcome.error
        plan.value = outcome.error || outcome.stopped ? undefined : outcome
      }
      if (plan.value) {
        // Check went from the footer while it ran, and the focus with it: the plan takes it, as the result does.
        await nextTick()
        banner.value?.focus()
      }
      return
    }

    // The dialog was closed while the copy ran and has been opened again since, for the selection of now: the
    // result belongs to the opening that started the copy.
    if (openedAs !== opening) {
      return
    }
    plan.value = undefined
    result.value = outcome
    copiedSelection = selection
    emit('copied', outcome)
    await nextTick()
    banner.value?.focus()
  } finally {
    running.value = undefined
    controller = undefined
  }
}

/**
 * Copy grows to the left when its plan appears, to where Check stood. Where the dialog is as high as the window its
 * footer stays in place, so the second click of a double click on Check lands on Copy, and would copy what the check
 * has only just shown, to the production view too. The click count of the event tells that click: it is not a
 * decision to copy. A key and a single click have a count of 0 or 1.
 */
function copyNow(event: MouseEvent): void {
  if (event.detail > 1) {
    return
  }
  void start(false)
}

/** Back to the options after a copy that did not finish: what it copied comes back Unchanged. */
function again(): void {
  result.value = undefined
}

async function compareNow(): Promise<void> {
  const source = context.scope
  const to = result.value?.target
  // The keys that were copied, not the selection as it is by now.
  const selection = copiedSelection
  if (!source || !to || !selection) {
    return
  }
  const set = fitsInAddress(selection.keys) ? undefined : store.remember(selection.keys)
  open.value = false
  await router.push(compareLocation(source, { ...selection, to, set }))
}

// The page was left while a copy ran: it ends after the key that is under way.
onBeforeUnmount(stop)
useEventListener(window, 'beforeunload', (event: BeforeUnloadEvent) => {
  if (running.value === 'copy') {
    event.preventDefault()
  }
})

// --- the documents of a check or of a copy ---

const shown = computed(() => result.value ?? plan.value)
const rows = computed(() => shown.value?.items ?? [])

const summary = computed(() => {
  const outcome = shown.value
  if (!outcome) {
    return undefined
  }
  const text = copySummary(outcome)
  const description = `${text.charAt(0).toUpperCase()}${text.slice(1)}.`
  if (outcome.dryRun) {
    return { title: 'Checked: nothing was written', description, color: 'info' as const, icon: 'i-lucide-list-checks' }
  }

  const counts = copyCounts(outcome.items)
  if (outcome.error) {
    return {
      title: `The copy to ${outcome.target} did not finish`,
      description: `${errorMessage(outcome.error)} ${description}`,
      color: 'error' as const,
      icon: 'i-lucide-circle-alert',
    }
  }
  if (outcome.stopped) {
    return {
      title: `The copy to ${outcome.target} was stopped`,
      description,
      color: 'warning' as const,
      icon: 'i-lucide-circle-stop',
    }
  }
  return counts.Failed + counts.Skipped > 0
    ? { title: `Copied to ${outcome.target}`, description, color: 'warning' as const, icon: 'i-lucide-triangle-alert' }
    : { title: `Copied to ${outcome.target}`, description, color: 'success' as const, icon: 'i-lucide-circle-check' }
})

/** The document in the target view, for what a copy wrote there. */
function linkOf(item: ViewCopyItem): string | undefined {
  const outcome = result.value
  if (!outcome || outcome.dryRun || (item.outcome !== 'Created' && item.outcome !== 'Updated')) {
    return undefined
  }
  const section = item.kind === 'Annotation' ? 'annotations' : 'configurations'
  return `/${context.org}/${context.project}/${outcome.target}/${section}/${encodeURIComponent(item.key)}`
}

/** The tables scroll: the region takes the focus, so keys scroll it too. */
const scrollRegion =
  'max-h-72 overflow-auto rounded-md border border-default focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none'
</script>

<template>
  <UModal
    v-model:open="open"
    class="ph-no-capture"
    :title="title"
    :description="`From ${context.org}/${context.project}, view ${context.view}.`"
    :dismissible="!running"
    :close="!running"
    :ui="{ content: 'max-w-3xl' }"
    @after:leave="emit('closed')"
  >
    <template #body>
      <div class="space-y-4">
        <template v-if="!result">
          <UAlert
            v-if="others.alone.value"
            color="neutral"
            variant="subtle"
            icon="i-lucide-info"
            title="The project has no other view"
            description="An Administrator can create another one on the Views page."
          />
          <UAlert
            v-else-if="lostTarget"
            color="warning"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            :title="`${target} is not another view of this project`"
            description="The comparison was made with a view that the project does not list any more."
          />
          <UAlert
            v-else-if="tooMany"
            color="warning"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            :title="`Select up to ${viewTransferLimit} to compare or copy them at once`"
          />

          <UFormField label="Copy to">
            <div class="flex items-center gap-2">
              <span v-if="fixed" class="text-sm font-medium text-highlighted">{{ fixed }}</span>
              <USelect
                v-else
                v-model="chosen"
                :items="others.names.value"
                placeholder="Pick a view"
                class="w-56"
                :disabled="!!running"
              />
              <UBadge v-if="production" v-bind="defaultViewBadge" />
            </div>
          </UFormField>

          <UAlert
            v-if="production"
            color="warning"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            :title="`${destination} is the production view`"
            description="The copy changes what production reads."
          />

          <!-- The label of the field names no control: the group of the two choices gets its name itself. -->
          <UFormField :label="`When ${there} already has it`">
            <URadioGroup
              v-model="existing"
              :items="existingItems"
              variant="card"
              :disabled="!!running"
              :aria-label="`When ${there} already has it`"
            />
          </UFormField>

          <UCheckbox
            v-if="kind === 'configurations'"
            v-model="missingAnnotations"
            label="Copy missing annotations too"
            :description="`A configuration needs its annotation. When ${there} does not have it, the annotation is copied first. Otherwise the configuration is skipped.`"
            :disabled="!!running"
          />

          <p class="text-sm text-muted">{{ copied }}</p>

          <UAlert
            v-if="failure"
            role="alert"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
            title="The check did not finish"
            :description="errorMessage(failure)"
          />
        </template>

        <!-- Mounted while empty too: a live region that appears with its text is often not read out. It takes room
             only while something runs. -->
        <div :class="running ? 'space-y-1' : 'sr-only'">
          <UProgress v-if="running && progress" size="sm" :model-value="progress.done" :max="progress.total" />
          <p :id="statusId" class="text-xs text-muted" role="status" aria-live="polite">{{ status }}</p>
        </div>

        <template v-if="summary">
          <div
            ref="banner"
            tabindex="-1"
            class="rounded-md focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <UAlert
              :color="summary.color"
              variant="subtle"
              :icon="summary.icon"
              :title="summary.title"
              :description="summary.description"
            />
          </div>

          <div
            v-if="rows.length"
            role="region"
            :aria-label="shown?.dryRun ? 'Checked documents' : 'Copied documents'"
            tabindex="0"
            :class="scrollRegion"
          >
            <!-- At least as wide as its four columns need: on a phone the region scrolls sideways instead of
                 breaking a key into a column of letters. -->
            <table class="w-full min-w-xl text-sm">
              <thead class="bg-elevated/50 text-xs text-muted">
                <tr>
                  <th class="px-3 py-2 text-start font-medium">Kind</th>
                  <th class="w-1/2 px-3 py-2 text-start font-medium">Key</th>
                  <th class="px-3 py-2 text-start font-medium">Outcome</th>
                  <th class="w-1/2 px-3 py-2 text-start font-medium">Reason</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                <tr v-for="(item, index) in rows" :key="`${index}:${item.kind}:${item.key}`">
                  <td class="px-3 py-1.5 text-xs whitespace-nowrap text-muted">
                    <span class="flex items-center gap-1.5">
                      <UIcon :name="documentKinds[item.kind].icon" class="size-4 shrink-0" />
                      {{ documentKinds[item.kind].label }}
                    </span>
                  </td>
                  <td class="px-3 py-1.5">
                    <!-- The link opens another view: the dialog closes with the view of the route, and stays for a
                         link that is opened in a new tab. -->
                    <RouterLink v-if="linkOf(item)" :to="linkOf(item)!" class="hover:underline">
                      <KeyText :value="item.key" :truncate="false" />
                    </RouterLink>
                    <KeyText v-else :value="item.key" :truncate="false" />
                  </td>
                  <td class="px-3 py-1.5">
                    <UBadge
                      :label="outcomeLabel(item.outcome, shown?.dryRun)"
                      :color="outcomes[item.outcome].color"
                      variant="subtle"
                      size="sm"
                    />
                  </td>
                  <td class="px-3 py-1.5 text-xs wrap-anywhere text-muted">{{ item.message }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </template>
      </div>
    </template>

    <template #footer>
      <div class="flex w-full flex-wrap items-center justify-end gap-2">
        <template v-if="result">
          <UButton
            v-if="result.error || result.stopped"
            label="Try again"
            icon="i-lucide-rotate-cw"
            color="neutral"
            variant="outline"
            @click="again"
          />
          <UButton
            v-else-if="compare"
            :label="`Compare with ${result.target}`"
            icon="i-lucide-git-compare"
            color="neutral"
            variant="outline"
            @click="compareNow"
          />
          <UButton label="Done" @click="open = false" />
        </template>

        <!-- Stop stands at the other end of the footer than the other buttons. Where Copy stood, the second click of
             a double click on Copy would land on it and stop the copy before its first key, and a click on Stop that
             comes as the run ends would land on Done or on the comparison. -->
        <template v-else-if="running">
          <UButton
            class="me-auto"
            label="Stop"
            icon="i-lucide-circle-stop"
            color="neutral"
            variant="outline"
            @click="stop"
          />
        </template>

        <template v-else>
          <p v-if="needsCheck && ready" :id="hintId" class="me-auto text-xs text-muted">
            Check first: {{ destination }} is the production view.
          </p>
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton
            label="Check"
            icon="i-lucide-list-checks"
            color="neutral"
            variant="outline"
            :disabled="!ready"
            @click="start(true)"
          />
          <UButton
            :label="copyLabel"
            icon="i-lucide-copy-plus"
            :disabled="!canCopy"
            :aria-describedby="needsCheck && ready ? hintId : undefined"
            @click="copyNow"
          />
        </template>
      </div>
    </template>
  </UModal>
</template>
