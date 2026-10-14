<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, useId, useTemplateRef, watch, type ComponentPublicInstance } from 'vue'
import { refDebounced } from '@vueuse/core'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore, type Scope } from '@/stores/context'
import { getAllAncestors, typeMeta } from '@/utils/annotation-key'
import { describeBulkProblem, readBulkLines, withArticle, type BulkLine } from '@/utils/bulk-keys'
import { sameView } from '@/utils/context-paths'
import { errorMessage } from '@/utils/errors'

/**
 * Bulk create (424-33): keys or compact lines, typed or pasted, reviewed against the catalog and sent to
 * SetAnnotationsSimple in batches of 100 (useAnnotationActions.createMany). The API creates the missing ancestors
 * and skips the existing keys, so the same list can be sent again after a failure. Only the canonical keys of the
 * review are sent, and an invalid line blocks the create, so nothing is written with a meaning the user did not see.
 * Deleted keys and ancestors are looked up first (useDeletedKeyCheck): restored, or re-created empty by the API.
 */
const open = defineModel<boolean>('open', { default: false })

const catalog = useCatalogStore()
const context = useContextStore()
const actions = useAnnotationActions()
const deletedCheck = useDeletedKeyCheck()

/** Non-blank lines of one run: ten requests. */
const maxLines = 1000
/** Lines the review table lists; the invalid ones are listed beyond it. */
const shownLines = 200

const placeholder = ['rst.invoicing', 'northwind', 'northwind.invoicing', 'northwind.invoicing.production'].join('\n')

/** The tables scroll, on a phone already with a few wrapped rows: the region takes the focus, so keys scroll it too. */
const scrollRegion =
  'max-h-72 overflow-auto rounded-md border border-default focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none'

type LineStatus = 'new' | 'exists' | 'duplicate' | 'invalid'

interface ReviewRow {
  line: BulkLine
  status: LineStatus
}

interface Review {
  rows: ReviewRow[]
  /** The keys of the New rows, in the order of the text. */
  newKeys: string[]
  /** Ancestors of the new keys that are neither in the catalog nor in the list; the API creates them. */
  missing: string[]
  counts: Record<LineStatus, number>
}

interface BulkResult {
  /** Created by the run, in the order of the responses: keys of the list and missing ancestors. */
  created: Annotation[]
  /** The keys of the list (the snapshot); every other created key is an ancestor. */
  listed: ReadonlySet<string>
  /** Keys that existed already: the Exists lines of the review and the keys the API skipped. */
  skipped: number
  /** Duplicate lines of the review, which were not sent. */
  repeated: number
  restored: number
}

const statusMeta: Record<LineStatus, { label: string; color: 'success' | 'neutral' | 'warning' | 'error' }> = {
  new: { label: 'New', color: 'success' },
  exists: { label: 'Exists', color: 'neutral' },
  duplicate: { label: 'Duplicate', color: 'warning' },
  invalid: { label: 'Invalid', color: 'error' },
}

const text = ref('')
// A long paste is read once, not on every keystroke; an emptied input clears the review at once.
const debouncedText = refDebounced(text, 150)
const creating = ref(false)
/** The review of the run: shown until it ends, so the rows do not change while the catalog takes the new keys. */
const snapshot = shallowRef<Review>()
const result = shallowRef<BulkResult>()
/** The error of the last run; `created` is set when createMany ran. */
const failure = ref<{ message: string; created?: number }>()

const input = useTemplateRef<{ textareaRef?: HTMLTextAreaElement | null }>('input')
const doneButton = useTemplateRef<ComponentPublicInstance>('doneButton')
// Done is described by the result, so a screen reader reads it with the focus; the failure is scrolled into view.
const resultId = useId()
const failureId = useId()

watch(open, (value) => {
  if (value) {
    text.value = ''
    snapshot.value = undefined
    result.value = undefined
    failure.value = undefined
  }
})

const number = new Intl.NumberFormat()
const counted = (count: number, singular: string, plural = `${singular}s`) =>
  `${number.format(count)} ${count === 1 ? singular : plural}`

/**
 * Each line against the catalog, which holds the active annotations (disabled and expired ones too), the same
 * "exists" as the API's. A legacy key with an upper-case code exists when the typed text is in the catalog.
 */
function reviewOf(lines: BulkLine[]): Review {
  const known = catalog.byKey
  const listed = new Set<string>()
  const counts: Record<LineStatus, number> = { new: 0, exists: 0, duplicate: 0, invalid: 0 }

  const rows = lines.map((line): ReviewRow => {
    const status: LineStatus = !line.key
      ? 'invalid'
      : listed.has(line.key)
        ? 'duplicate'
        : known.has(line.key) || known.has(line.text)
          ? 'exists'
          : 'new'
    if (line.key) {
      listed.add(line.key)
    }
    counts[status]++
    return { line, status }
  })

  const newKeys = rows.filter((row) => row.status === 'new').map((row) => row.line.key!)
  const missing = new Set<string>()
  for (const key of newKeys) {
    for (const ancestor of getAllAncestors(key)) {
      if (!known.has(ancestor) && !listed.has(ancestor)) {
        missing.add(ancestor)
      }
    }
  }

  return { rows, newKeys, missing: [...missing], counts }
}

const live = computed(() => reviewOf(readBulkLines(text.value.trim() ? debouncedText.value : '')))
const review = computed(() => snapshot.value ?? live.value)

const canCreate = (target: Review) =>
  target.newKeys.length > 0 && target.counts.invalid === 0 && target.rows.length <= maxLines

const tooMany = computed(() =>
  live.value.rows.length > maxLines
    ? `At most ${number.format(maxLines)} keys at once; the list has ${number.format(live.value.rows.length)}.`
    : undefined,
)

const summary = computed(() => {
  const { counts, missing } = review.value
  const parts: [number, string][] = [
    [counts.new, `${number.format(counts.new)} new`],
    [counts.exists, `${number.format(counts.exists)} already ${counts.exists === 1 ? 'exists' : 'exist'}`],
    [counts.duplicate, counted(counts.duplicate, 'duplicate')],
    [counts.invalid, `${number.format(counts.invalid)} invalid`],
    [missing.length, counted(missing.length, 'missing ancestor')],
  ]
  return parts
    .filter(([count]) => count > 0)
    .map(([, part]) => part)
    .join(' · ')
})

const shownRows = computed(() =>
  review.value.rows.filter((row, index) => index < shownLines || row.status === 'invalid'),
)
const hiddenRows = computed(() => review.value.rows.length - shownRows.value.length)

// The statuses are only as good as the catalog; the API skips existing keys anyway, and the result counts come
// from its response.
const catalogNote = computed(() => {
  if (catalog.status === 'error' || catalog.error) {
    return 'The catalog could not be loaded, so the keys could not be checked: a key shown as new may exist already; the API skips it.'
  }
  if (catalog.status !== 'ready' || catalog.fromSnapshot) {
    return 'The catalog is still loading: a key shown as new may exist already; the API skips it.'
  }
  return undefined
})

// The review, not the live one: during a run the catalog takes the created keys, which would count the label down.
const createLabel = computed(() =>
  failure.value ? 'Try again' : `Create ${counted(review.value.newKeys.length, 'annotation')}`,
)

const progress = computed(() => {
  const value = actions.progress.value
  return creating.value && value && value.total > 100 ? value : undefined
})

const failureNote = computed(() => {
  const created = failure.value?.created
  if (created === undefined) {
    return undefined
  }
  const again = 'creating the same list again is safe, because existing keys are skipped.'
  return created > 0
    ? `At least ${counted(created, 'annotation')} ${created === 1 ? 'was' : 'were'} created before the error; ${again}`
    : `${again.charAt(0).toUpperCase()}${again.slice(1)}`
})

const resultTitle = computed(() => {
  const created = result.value?.created ?? []
  const fromList = created.filter((annotation) => result.value?.listed.has(annotation.AnnotationKey)).length
  const ancestors = created.length - fromList
  if (created.length === 0) {
    return 'No annotations were created.'
  }
  return ancestors === 0
    ? `Created ${counted(created.length, 'annotation')}.`
    : `Created ${counted(created.length, 'annotation')}: ${number.format(fromList)} from the list and ${counted(ancestors, 'missing ancestor')}.`
})

const resultDescription = computed(() => {
  const { skipped = 0, repeated = 0, restored = 0 } = result.value ?? {}
  const sentences = [
    skipped > 0 &&
      `${counted(skipped, 'key')} already ${skipped === 1 ? 'existed and was' : 'existed and were'} skipped.`,
    repeated > 0 && `${counted(repeated, 'repeated line')} ${repeated === 1 ? 'was' : 'were'} left out.`,
    restored > 0 && `${counted(restored, 'deleted annotation')} ${restored === 1 ? 'was' : 'were'} restored.`,
  ]
  return sentences.filter((sentence) => !!sentence).join(' ') || undefined
})

function focusInput() {
  input.value?.textareaRef?.focus()
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
    event.preventDefault()
    void create()
  }
}

function removeInvalid() {
  const invalid = new Set(
    readBulkLines(text.value)
      .filter((line) => !line.key)
      .map((line) => line.line),
  )
  text.value = text.value
    .split(/\r?\n/)
    .filter((_, index) => !invalid.has(index + 1))
    .join('\n')
  // The button goes away with the invalid lines; the input stays, with the lines left to check.
  focusInput()
}

async function createMore() {
  text.value = ''
  result.value = undefined
  failure.value = undefined
  await nextTick()
  focusInput()
}

async function create() {
  // The text as it is now, not as the debounced review last read it.
  const current = reviewOf(readBulkLines(text.value))
  const target = context.scope
  if (creating.value || !target || !canCreate(current)) {
    return
  }

  creating.value = true
  failure.value = undefined
  snapshot.value = current

  try {
    await run(current, target)
  } catch (error) {
    failure.value = { message: errorMessage(error) }
  } finally {
    creating.value = false
    snapshot.value = undefined
  }

  // The focus follows the step: Done on the result, the input after an error or a Cancel. The Create button was
  // disabled while it ran, which dropped the focus, so it is always placed.
  await nextTick()
  if (result.value) {
    const done = doneButton.value?.$el as HTMLElement | undefined
    done?.focus()
  } else {
    focusInput()
  }
  if (failure.value) {
    document.getElementById(failureId)?.scrollIntoView({ block: 'nearest' })
  }
}

/**
 * One run over the reviewed keys: sets the result, or the failure when a batch fails. Every step writes into the
 * view that was reviewed; the deleted-key question waits for the user, who may move to another view meanwhile.
 */
async function run(current: Review, target: Scope) {
  const stayInView = () => {
    if (!context.scope || !sameView(context.scope, target)) {
      throw new Error(
        `You moved to another view, so the keys were not created in ${target.org}/${target.project}, view ${target.view}.`,
      )
    }
  }

  const { choice, deleted } = await deletedCheck.check(current.newKeys)
  if (choice === 'cancel') {
    return
  }
  stayInView()

  // Restore: the deleted keys and ancestors come back alone, with their content, and are not created again.
  const created: Annotation[] = []
  let restored = 0
  let keys = current.newKeys
  if (choice === 'restore') {
    const outcome = await actions.restoreMany(
      deleted.map((annotation: Annotation) => annotation.AnnotationKey),
      { descendants: false },
    )
    if (outcome.failed.length) {
      throw new Error(outcome.failed.map((failed) => `${failed.Key}: ${failed.Message}`).join(' '))
    }
    // a missing ancestor of a restored key is created by the restore, not restored
    const createdKeys = new Set(outcome.created)
    for (const annotation of outcome.restored) {
      if (createdKeys.has(annotation.AnnotationKey)) {
        created.push(annotation)
      } else {
        restored++
      }
    }
    const back = new Set(outcome.restored.map((annotation) => annotation.AnnotationKey))
    keys = keys.filter((key) => !back.has(key))
    stayInView()
  }

  let skipped: string[] = []
  if (keys.length) {
    const outcome = await actions.createMany(keys)
    created.push(...outcome.created)
    if (outcome.failure) {
      // the ancestors a restore created count too: they were written before the error
      failure.value = { message: errorMessage(outcome.failure), created: created.length }
      return
    }
    skipped = outcome.skipped
  }

  result.value = {
    created,
    listed: new Set(current.newKeys),
    skipped: current.counts.exists + skipped.length,
    repeated: current.counts.duplicate,
    restored,
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="Bulk create annotations"
    :description="`In ${context.org}/${context.project}, view ${context.view}.`"
    :dismissible="!creating"
    :close="!creating"
    :ui="{ content: 'max-w-3xl' }"
  >
    <template #body>
      <div class="space-y-4">
        <template v-if="result">
          <UAlert
            :id="resultId"
            color="success"
            variant="subtle"
            icon="i-lucide-check"
            :title="resultTitle"
            :description="resultDescription"
          />
          <div
            v-if="result.created.length"
            role="region"
            aria-label="Created annotations"
            tabindex="0"
            :class="scrollRegion"
          >
            <table class="w-full text-sm">
              <thead class="bg-elevated/50 text-xs text-muted">
                <tr>
                  <th class="w-full px-3 py-2 text-start font-medium">Key</th>
                  <th class="px-3 py-2 text-start font-medium whitespace-nowrap">Created as</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-default">
                <tr v-for="annotation in result.created" :key="annotation.AnnotationKey">
                  <td class="px-3 py-1.5">
                    <div class="flex min-w-0 items-center gap-2">
                      <AnnotationTypeBadge :type="annotation.AnnotationType" variant="label" />
                      <KeyText :value="annotation.AnnotationKey" :truncate="false" />
                    </div>
                  </td>
                  <td class="px-3 py-1.5 text-xs whitespace-nowrap text-muted">
                    {{ result.listed.has(annotation.AnnotationKey) ? 'From the list' : 'Missing ancestor' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </template>

        <template v-else>
          <UAlert color="neutral" variant="subtle" icon="i-lucide-info" title="How bulk create works">
            <template #description>
              <ul class="mt-1 list-disc space-y-1 ps-4">
                <li>
                  One key per line. A line that starts with a type code is that key, for example
                  <code class="font-mono">rst.invoicing</code>.
                </li>
                <li>
                  A line without a code is read by its number of parts:
                  <code class="font-mono">northwind</code> is {{ withArticle(typeMeta.sbt.label) }},
                  <code class="font-mono">northwind.invoicing</code> {{ withArticle(typeMeta.usg.label) }},
                  <code class="font-mono">northwind.invoicing.production</code> {{ withArticle(typeMeta.exe.label) }},
                  and four parts {{ withArticle(typeMeta.uxe.label) }}. {{ typeMeta.rst.plural }},
                  {{ typeMeta.cnt.plural.toLowerCase() }} and {{ typeMeta.unt.plural.toLowerCase() }} need their code.
                </li>
                <li>
                  Missing ancestors are created with them. Keys that already exist are skipped, so the same list can be
                  sent again.
                </li>
                <li>The new annotations get only their key and name; add titles, labels and configurations later.</li>
              </ul>
            </template>
          </UAlert>

          <!-- Above the input, not below the review: on a phone the end of the body is out of view. -->
          <UAlert
            v-if="failure"
            :id="failureId"
            role="alert"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
          >
            <template #description>
              <p>{{ failure.message }}</p>
              <p v-if="failureNote" class="mt-1">{{ failureNote }}</p>
            </template>
          </UAlert>

          <UFormField label="Keys" help="One key per line, up to 1,000." :error="tooMany">
            <UTextarea
              ref="input"
              v-model="text"
              class="w-full font-mono"
              :rows="8"
              autoresize
              :maxrows="16"
              autofocus
              :readonly="creating"
              :placeholder="placeholder"
              @keydown="onKeydown"
            />
          </UFormField>

          <!-- Mounted while empty too: a live region that appears with its text is often not read out, and the
               first summary is the one of the paste. -->
          <p class="text-sm" aria-live="polite">{{ summary }}</p>
          <template v-if="review.rows.length">
            <UAlert
              v-if="catalogNote"
              color="warning"
              variant="subtle"
              icon="i-lucide-triangle-alert"
              :description="catalogNote"
            />
            <div role="region" aria-label="Review" tabindex="0" :class="scrollRegion">
              <!-- The key takes the spare width; the status column is as wide as its badge, or as a problem message
                   needs (it wraps anywhere, as it holds long dotted words that would widen the column). -->
              <table class="w-full text-sm">
                <thead class="bg-elevated/50 text-xs text-muted">
                  <tr>
                    <th class="px-3 py-2 text-start font-medium">Line</th>
                    <th class="w-full px-3 py-2 text-start font-medium">Key</th>
                    <th class="px-3 py-2 text-start font-medium">Status</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-default">
                  <tr
                    v-for="row in shownRows"
                    :key="row.line.line"
                    :class="row.status === 'invalid' ? 'bg-error/5' : ''"
                  >
                    <td class="px-3 py-1.5 text-xs text-muted tabular-nums">{{ row.line.line }}</td>
                    <td class="px-3 py-1.5">
                      <span v-if="!row.line.key" class="font-mono text-xs break-all text-error">{{
                        row.line.text
                      }}</span>
                      <div v-else class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5">
                        <AnnotationTypeBadge :type="row.line.key.slice(0, 3)" variant="label" />
                        <KeyText :value="row.line.key" :truncate="false" />
                        <span v-if="row.line.text !== row.line.key" class="font-mono text-xs break-all text-muted">
                          {{ row.line.text }}
                        </span>
                      </div>
                    </td>
                    <td class="px-3 py-1.5">
                      <div class="flex flex-wrap items-center gap-2">
                        <UBadge
                          :label="statusMeta[row.status].label"
                          :color="statusMeta[row.status].color"
                          variant="subtle"
                          size="sm"
                        />
                        <span
                          v-if="row.status === 'invalid'"
                          class="min-w-28 text-xs wrap-anywhere text-error sm:min-w-56"
                        >
                          {{ describeBulkProblem(row.line) }}
                        </span>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
              <p v-if="hiddenRows" class="border-t border-default px-3 py-2 text-xs text-muted">
                and {{ counted(hiddenRows, 'more line') }}
              </p>
            </div>
          </template>

          <div v-if="progress" class="space-y-1">
            <UProgress size="sm" :model-value="progress.done" :max="progress.total" />
            <p class="text-xs text-muted">
              Sent {{ number.format(progress.done) }} of {{ counted(progress.total, 'key') }}…
            </p>
          </div>
        </template>

        <DeletedKeysDialog
          v-model:open="deletedCheck.open.value"
          :deleted="deletedCheck.deleted.value"
          restore-label="Restore"
          @choose="deletedCheck.choose"
        />
      </div>
    </template>

    <template #footer>
      <div class="flex w-full flex-wrap justify-end gap-2">
        <template v-if="result">
          <UButton
            label="Create more"
            icon="i-lucide-list-plus"
            color="neutral"
            variant="outline"
            @click="createMore"
          />
          <UButton ref="doneButton" label="Done" :aria-describedby="resultId" @click="open = false" />
        </template>
        <template v-else>
          <UButton label="Cancel" color="neutral" variant="ghost" :disabled="creating" @click="open = false" />
          <UButton
            v-if="live.counts.invalid && !creating"
            label="Remove invalid lines"
            icon="i-lucide-list-x"
            color="neutral"
            variant="outline"
            @click="removeInvalid"
          />
          <UButton
            :label="createLabel"
            icon="i-lucide-list-plus"
            :loading="creating"
            :disabled="!canCreate(review)"
            @click="create"
          />
        </template>
      </div>
    </template>
  </UModal>
</template>
