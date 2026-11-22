<script setup lang="ts">
import { computed, onMounted, ref, shallowRef, useId, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { filtersOf, stateOf, useViewCompare, type CompareFile } from '@/composables/useViewCompare'
import { useContextStore } from '@/stores/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import {
  compareFilters,
  compareLabel,
  compareLocation,
  copyChanges,
  parseCompareQuery,
  viewTransferLimit,
  type CompareFilter,
} from '@/utils/view-compare'
import type { ViewCopyResult } from '@/utils/view-copy'
import { countOf } from '@/utils/view-documents'
import { defaultViewBadge, isDefaultView } from '@/utils/views'

// The comparison of a selection of annotations or configurations across two views of the project, as the "Files
// changed" of a pull request (spec "Copy and Compare Annotations and Configurations Between Views", sections 3 to 5).
// The source is the view of the route, where the rows were selected, and the target the view of ?to=. The page reads
// from the source into the target: the target is the old side of every diff (left, red), the source the new one
// (right, green). That is the opposite of Compare views, whose left side is the view of the route. Addressed by
// ?kind=&to=&key=…&content=, or by ?set= for a selection that is too long for an address.
const route = useRoute()
const router = useRouter()
const context = useContextStore()
const transfer = useViewTransferStore()
const others = useOtherViews()

// The view of this page is the one of its route, which Swap views changes together with ?to=. The context guard puts
// the next org, project and view in the store before a navigation lands; until the store and the route agree again,
// nothing is preselected or read, as a router.replace would cancel that navigation (424-64).
const page = computed(() => route.params as { org?: string; project?: string; view?: string })
const landed = computed(
  () => page.value.org === context.org && page.value.project === context.project && page.value.view === context.view,
)

const request = computed(() => parseCompareQuery(route.query, page.value.view, transfer.keysOf))
const kind = computed(() => request.value.kind)
const viewBase = computed(() => `/${context.org}/${context.project}/${context.view}`)

const target = computed({
  get: () => request.value.to,
  set: (view) => {
    if (!view) {
      return
    }
    void router.replace({ query: { ...route.query, to: view } })
    if (context.projectKey) {
      transfer.rememberTarget(context.projectKey, view)
    }
  },
})

// With one other view there is nothing to pick; with several, the target last used in the project is the likely one.
// A ?to= that names a view is kept, also one that is not listed: the Views page can link to a discovered view.
watch(
  [others.names, target, landed],
  ([names, to, here]) => {
    if (!here || to) {
      return
    }
    const last = context.projectKey ? transfer.lastTarget(context.projectKey) : undefined
    const preselected = names.length === 1 ? names[0] : names.find((name) => name === last)
    if (preselected) {
      void router.replace({ query: { ...route.query, to: preselected } })
    }
  },
  { immediate: true },
)

/** The other views, and the compared one when it is not among them, so the select shows what is compared. */
const targetItems = computed(() =>
  target.value && !others.has(target.value) ? [...others.names.value, target.value] : others.names.value,
)

// The same comparison from the target: only this turns a comparison around, the view switcher opens Compare views.
const swap = computed(() =>
  context.scope && target.value
    ? compareLocation(
        { ...context.scope, view: target.value },
        {
          kind: kind.value,
          keys: request.value.keys,
          to: context.scope.view,
          content: request.value.content,
          set: request.value.set,
        },
      )
    : undefined,
)

const content = computed({
  get: () => request.value.content,
  set: (value: string | number) =>
    void router.replace({ query: { ...route.query, content: value === 'effective' ? 'effective' : undefined } }),
})
const contentItems = [
  { label: 'Own', value: 'own' },
  { label: 'Effective', value: 'effective' },
]

const layout = ref<'auto' | 'split' | 'unified'>('auto')
const layoutItems = [
  { label: 'Auto', value: 'auto' },
  { label: 'Split', value: 'split' },
  { label: 'Unified', value: 'unified' },
]

const { files, loaded } = useViewCompare({
  source: () => context.scope,
  target,
  kind,
  keys: () => request.value.keys,
  content: () => request.value.content,
  enabled: landed,
})

const title = computed(() => {
  const count = countOf(kind.value, request.value.keys.length)
  return target.value ? `${count}: ${context.view} → ${target.value}` : `${count}: ${context.view}`
})
const back = computed(() => ({
  label: kind.value === 'annotations' ? 'Annotations' : 'Configurations',
  to: `${viewBase.value}/${kind.value}`,
}))

const names = computed(() => ({
  source: context.view ?? '',
  target: target.value ?? '',
  own: kind.value === 'configurations' && request.value.content === 'own',
}))
const legend = computed(() =>
  kind.value === 'configurations' && request.value.content === 'effective'
    ? `What ${names.value.target} and ${names.value.source} read. A copy moves only own configurations.`
    : 'Only the selected keys are compared. A copy moves what is green and never deletes: a document that only ' +
      `${names.value.target} has stays.`,
)
const progress = computed(() =>
  loaded.value < files.value.length ? `Compared ${loaded.value} of ${files.value.length}…` : '',
)

// --- summary ---

const counts = computed(() => {
  const totals = new Map<CompareFilter, number>([['all', files.value.length]])
  for (const file of files.value) {
    for (const filter of filtersOf(file)) {
      totals.set(filter, (totals.get(filter) ?? 0) + 1)
    }
  }
  return totals
})
/** The chips with their counts; a result that no key has is not offered. */
const chips = computed(() =>
  compareFilters
    .map((filter) => ({ filter, label: compareLabel(filter, names.value), count: counts.value.get(filter) ?? 0 }))
    .filter((chip) => chip.filter === 'all' || chip.count > 0),
)

const filter = ref<CompareFilter>('all')
// A chip that lost its last key is gone, and its filter would show nothing.
watch(
  () => counts.value.get(filter.value),
  (count) => {
    if (!count) {
      filter.value = 'all'
    }
  },
)
const shown = computed(() =>
  filter.value === 'all' ? files.value : files.value.filter((file) => filtersOf(file).includes(filter.value)),
)

// --- copy ---

/**
 * Whether a copy from the source would change the key in the target: its annotations differ, or its own
 * configurations do, whatever is shown. A key that is deleted in the target is never copied over.
 */
const copies = (file: CompareFile) => !file.deleted && file.error === undefined && copyChanges(file.own)
/** What the active filter shows and a copy would change. */
const copyable = computed(() => shown.value.filter(copies))

const blockedId = useId()
// A copy goes only to a view that the project lists: a write to any other name would create that view.
const copyBlocked = computed(() => {
  if (!others.has(target.value)) {
    return others.known.value ? `${target.value} is not a view of this project` : undefined
  }
  return loaded.value === files.value.length && copyable.value.length === 0 ? 'Nothing to copy' : undefined
})
const copyLabel = computed(() =>
  copyable.value.length
    ? `Copy ${new Intl.NumberFormat().format(copyable.value.length)} to ${target.value}`
    : `Copy to ${target.value}`,
)

const copying = ref(false)
const copyKeys = ref<string[]>([])
/**
 * What this page showed of the target when the copy was opened, by key: the copy skips a key whose target changed
 * since. Not what it shows by now: a check makes the page read a changed key again, and the copy that follows the
 * check has to skip that key, as the check said it would.
 */
const expected = shallowRef<Record<string, string | number | null>>({})

/** The button that opened the copy; it is gone, or off, once its keys are copied. */
let opener: HTMLElement | null = null
/** The dialog gave the focus back to the opener, which the next read of the target may still take away. */
let returned = false

function copy(keys: string[]) {
  // A click does not focus a button in every browser: the body is no opener to return to.
  const active = document.activeElement
  opener = active instanceof HTMLElement && active !== document.body ? active : null
  returned = false
  copyKeys.value = keys
  expected.value = Object.fromEntries(
    files.value.filter((file) => keys.includes(file.key)).map((file) => [file.key, file.expected]),
  )
  copying.value = true
}

/** What the copy wrote is its own change: run again after it ended early, it finds those keys unchanged. */
function copied(result: ViewCopyResult) {
  const selected = kind.value === 'annotations' ? 'Annotation' : 'Configuration'
  const written = new Set(
    result.items
      .filter((item) => item.kind === selected && (item.outcome === 'Created' || item.outcome === 'Updated'))
      .map((item) => item.key),
  )
  expected.value = Object.fromEntries(Object.entries(expected.value).filter(([key]) => !written.has(key)))
}

// --- sections ---

/** Different documents, and a document on one side only, are what the page is opened for. */
function startsOpen(file: CompareFile): boolean {
  const state = stateOf(file)
  return state === 'different' || state === 'onlySource' || state === 'onlyTarget'
}

// The open state of a section by key: set when its documents first arrive, and by the user from then on. So a
// section keeps its state when its documents are read again and compare differently.
const opened = ref<Record<string, boolean>>({})
const isOpen = (file: CompareFile) => opened.value[file.key] ?? startsOpen(file)

/**
 * The dialog returns the focus to the button that opened it. When that button is gone or off (its keys are copied),
 * the focus would fall to the page: it goes to the section of the copied key, or to the title of the page.
 */
function closed() {
  if (takesFocus(opener)) {
    returned = true
    return
  }
  focusHome()
}

const takesFocus = (element: HTMLElement | null) => !!element?.isConnected && !(element as HTMLButtonElement).disabled

function focusHome() {
  const key = copyKeys.value[0]
  const section = key
    ? root.value?.querySelector<HTMLElement>(`section[data-key="${CSS.escape(key)}"] h3 button`)
    : undefined
  const home = section ?? heading.value
  home?.focus()
}

// The page reads the target again after a copy, also after the dialog is gone: the opener that got the focus back
// goes, or turns off, only then.
watch(
  files,
  () => {
    if (!returned || copying.value || takesFocus(opener)) {
      return
    }
    returned = false
    if (document.activeElement === document.body || document.activeElement === opener) {
      focusHome()
    }
  },
  { flush: 'post' },
)

// The copy was opened for the comparison as it was: its keys and what it saw of the target hold for no other.
watch(
  [() => context.org, () => context.project, () => context.view, target, kind, () => request.value.keys.join('\n')],
  () => {
    copying.value = false
    copyKeys.value = []
    expected.value = {}
  },
)

// Another comparison starts with its own sections.
watch([() => context.org, () => context.project, () => context.view, target, kind, () => request.value.content], () => {
  opened.value = {}
})
// After the render: the results of the queries follow a change of the keys in a watcher of their own, and until
// then a file could pair a key with the documents of another one.
watch(
  files,
  (list) => {
    const arrived = list.filter((file) => file.shown && !(file.key in opened.value))
    if (arrived.length) {
      opened.value = { ...opened.value, ...Object.fromEntries(arrived.map((file) => [file.key, startsOpen(file)])) }
    }
  },
  { immediate: true, flush: 'post' },
)

function toggle(file: CompareFile) {
  opened.value = { ...opened.value, [file.key]: !isOpen(file) }
}

function setAll(open: boolean) {
  opened.value = Object.fromEntries(files.value.map((file) => [file.key, open]))
}

// The panel body scrolls, not the window. The sections watch for their place in it to build their editors.
const root = useTemplateRef<HTMLElement>('root')
const heading = useTemplateRef<HTMLElement>('heading')
const scrollRoot = shallowRef<HTMLElement | null>(null)
onMounted(() => {
  for (let parent = root.value?.parentElement; parent; parent = parent.parentElement) {
    const overflow = getComputedStyle(parent).overflowY
    if (overflow === 'auto' || overflow === 'scroll') {
      scrollRoot.value = parent
      return
    }
  }
})
</script>

<template>
  <UDashboardPanel id="compare-selection">
    <template #header>
      <ModelingHeader />
    </template>
    <template #body>
      <div ref="root" class="space-y-4">
        <template v-if="landed && context.scope">
          <EmptyState
            v-if="request.keys.length === 0"
            icon="i-lucide-git-compare"
            title="Nothing to compare"
            :description="
              request.lost
                ? 'The selection is gone. A comparison of many rows opens only in the browser tab that started it.'
                : 'Select rows on Annotations or Configurations and choose Compare with view.'
            "
          >
            <UButton
              label="Annotations"
              icon="i-lucide-tags"
              color="neutral"
              variant="outline"
              :to="`${viewBase}/annotations`"
            />
            <UButton
              label="Configurations"
              icon="i-lucide-settings-2"
              color="neutral"
              variant="outline"
              :to="`${viewBase}/configurations`"
            />
          </EmptyState>

          <template v-else>
            <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
              <h2 ref="heading" tabindex="-1" class="text-base font-semibold text-highlighted focus-visible:outline-none">
                {{ title }}
              </h2>
              <UButton
                :label="back.label"
                :to="back.to"
                icon="i-lucide-arrow-left"
                color="neutral"
                variant="link"
                size="sm"
                class="px-0"
              />
            </div>

            <!-- Without another view the Compare views tab is disabled, but an address still opens the page. -->
            <EmptyState
              v-if="!target && others.alone.value"
              icon="i-lucide-git-compare"
              title="No other view to compare with"
              description="The project has only this view. An Administrator can create another one on the Views page."
            />

            <template v-else>
              <!-- The sides in the order of the diff, which also holds where the diff has one column. -->
              <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
                <div class="flex items-center gap-2">
                  <span class="font-mono text-base font-semibold text-error" aria-hidden="true">−</span>
                  <USelect
                    v-model="target"
                    :items="targetItems"
                    placeholder="Another view"
                    class="w-48"
                    aria-label="Target view"
                  />
                  <UBadge v-if="isDefaultView(target)" v-bind="defaultViewBadge" />
                  <span class="text-xs text-muted">now</span>
                </div>
                <UButton
                  label="Swap views"
                  icon="i-lucide-arrow-left-right"
                  color="neutral"
                  variant="outline"
                  size="sm"
                  :to="swap"
                  :disabled="!swap"
                />
                <div class="flex items-center gap-2">
                  <span class="font-mono text-base font-semibold text-success" aria-hidden="true">+</span>
                  <span class="text-sm font-medium text-highlighted">{{ context.view }}</span>
                  <span class="text-xs text-muted">after a copy</span>
                </div>
              </div>

              <UAlert
                v-if="request.truncated"
                color="warning"
                variant="subtle"
                icon="i-lucide-triangle-alert"
                :title="`Only the first ${viewTransferLimit} keys are compared.`"
              />

              <LoadingState v-if="!target && others.views.isPending.value" />
              <EmptyState v-else-if="!target" icon="i-lucide-git-compare" title="Pick the view to compare with" />

              <template v-else>
                <p class="text-sm text-muted">{{ legend }}</p>

                <div class="flex flex-wrap items-center gap-2">
                  <div v-if="kind === 'configurations'" role="group" aria-label="Configuration content">
                    <UTabs v-model="content" :items="contentItems" :content="false" size="xs" />
                  </div>
                  <div role="group" aria-label="Diff layout">
                    <UTabs v-model="layout" :items="layoutItems" :content="false" size="xs" />
                  </div>
                  <UButton
                    label="Expand all"
                    icon="i-lucide-unfold-vertical"
                    color="neutral"
                    variant="outline"
                    size="sm"
                    @click="setAll(true)"
                  />
                  <UButton
                    label="Collapse all"
                    icon="i-lucide-fold-vertical"
                    color="neutral"
                    variant="outline"
                    size="sm"
                    @click="setAll(false)"
                  />
                  <!--
                    What the filter shows and a copy would change; the reason is text too, for a keyboard. As in the
                    sections, there is no copy until the target is known to be a view of the project.
                  -->
                  <div class="ms-auto flex flex-wrap items-center gap-2">
                    <span v-if="copyBlocked" :id="blockedId" class="text-xs text-muted">{{ copyBlocked }}</span>
                    <UTooltip :text="copyBlocked" :disabled="!copyBlocked">
                      <UButton
                        :label="copyLabel"
                        icon="i-lucide-copy-plus"
                        size="sm"
                        :disabled="!!copyBlocked || copyable.length === 0 || !others.has(target)"
                        :aria-describedby="copyBlocked ? blockedId : undefined"
                        @click="copy(copyable.map((file) => file.key))"
                      />
                    </UTooltip>
                  </div>
                </div>

                <div class="flex flex-wrap items-center gap-x-3 gap-y-2">
                  <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by result">
                    <UButton
                      v-for="chip in chips"
                      :key="chip.filter"
                      :label="`${chip.label} ${chip.count}`"
                      size="sm"
                      :color="filter === chip.filter ? 'primary' : 'neutral'"
                      :variant="filter === chip.filter ? 'soft' : 'outline'"
                      :aria-pressed="filter === chip.filter"
                      @click="filter = filter === chip.filter ? 'all' : chip.filter"
                    />
                  </div>
                  <!-- There before the first document arrives, so every change of it is read out. -->
                  <p class="text-sm text-muted" aria-live="polite">{{ progress }}</p>
                </div>

                <div class="space-y-3">
                  <ViewCompareFile
                    v-for="file in shown"
                    :key="file.key"
                    :file="file"
                    :kind="kind"
                    :content="request.content"
                    :source="context.scope"
                    :target="target"
                    :layout="layout"
                    :open="isOpen(file)"
                    :copyable="copies(file) && others.has(target)"
                    :scroll-root="scrollRoot"
                    @toggle="toggle(file)"
                    @copy="copy([file.key])"
                  />
                </div>

                <!-- The target is the compared one, and the copy skips what changed there since it was shown here. -->
                <ViewCopyDialog
                  v-model:open="copying"
                  :kind="kind"
                  :keys="copyKeys"
                  :target="target"
                  :expected="expected"
                  :compare="false"
                  @copied="copied"
                  @closed="closed"
                />
              </template>
            </template>
          </template>
        </template>
      </div>
    </template>
  </UDashboardPanel>
</template>
