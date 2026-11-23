<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import {
  acceptViewInboxItem,
  dismissViewInboxItem,
  getViewInbox,
  hasErrorCode,
  isForbidden,
  isNotFound,
  isStorytellerError,
  type ViewInboxItem,
} from '@42for.net/2splatform.sdk'
import { invalidateInbox, useViewInbox } from '@/queries/inbox'
import { useContextStore, type Scope } from '@/stores/context'
import { useTransfersStore } from '@/stores/transfers'
import { errorMessage, writeFailureTitle } from '@/utils/errors'
import { inboxAcceptConfirmation, inboxEmptyState, type InboxFilter } from '@/utils/inbox'

// Inbox of the view (424-31, docs/Platform/Storyteller/inbox.md): what the platform left for the people of the
// project. The results of background jobs (the ends of exports and imports, for Administrators), the actions it
// suggests, to accept or dismiss, and plain messages. Two tabs, kept in ?tab= (Open without it): the open items, and
// under Done the accepted and dismissed ones. An item is open for everybody who sees it until one of them closes it.
// The API serves the inbox from Contributor up: below it the page shows a lock and asks for nothing, and it shows the
// same lock when the list answers 403, because the role was lost while the page was open.
//
// While the page is open it reads the open items again every 20 seconds, whatever the tab: the count of the Open tab
// and the mark of the menu come from that answer, which the sidebar shares. The Done list is a second query, read
// while its tab is shown, on Refresh and after every accept and dismiss.
const route = useRoute()
const router = useRouter()
const context = useContextStore()
const transfers = useTransfersStore()
const queryClient = useQueryClient()
const confirm = useConfirm()
const toast = useToast()
const canUseInbox = useCan('edit')
const isAdministrator = useCan('Administrator')

const tab = computed({
  get: () => (route.query.tab === 'done' ? 'done' : 'open'),
  set: (value: string) =>
    void router.replace({ query: { ...route.query, tab: value === 'done' ? 'done' : undefined } }),
})
const filter = computed<InboxFilter>(() => (tab.value === 'done' ? 'Done' : 'Open'))

const open = useViewInbox(() => context.scope, 'Open', { enabled: canUseInbox, poll: true })
const done = useViewInbox(() => context.scope, 'Done', { enabled: () => canUseInbox.value && filter.value === 'Done' })
const shown = computed(() => (filter.value === 'Done' ? done : open))
const page = computed(() => shown.value.data.value)
const isPending = computed(() => shown.value.isPending.value)
const failure = computed(() => (shown.value.isError.value ? shown.value.error.value : undefined))

// A 403 of the open items, which are always read, or of the list that is shown.
const forbidden = computed(() => isForbidden(open.error.value) || isForbidden(failure.value))
const locked = computed(() => !canUseInbox.value || forbidden.value)

// As the mark of the menu: no count while the answer it came from is replaced by an error.
const openCount = computed(() => (open.isError.value ? 0 : (open.data.value?.OpenCount ?? 0)))
const tabs = computed(() => [
  { label: 'Open', value: 'open', icon: 'i-lucide-inbox', badge: openCount.value > 0 ? openCount.value : undefined },
  { label: 'Done', value: 'done', icon: 'i-lucide-archive' },
])

const empty = computed(() => inboxEmptyState(filter.value, isAdministrator.value))

const more = ref<ViewInboxItem[]>([])
const nextToken = ref<string | null>()
const loadingMore = ref(false)
// The pages of Load more continue behind the first page they were asked for. Another first page (the other tab,
// another view, or a re-read that changed it) starts the list again from it, and a page that arrives for an older
// one is dropped. A re-read with the same answer keeps its object, so it keeps what was loaded.
let generation = 0

watch(
  page,
  (first) => {
    generation++
    more.value = []
    loadingMore.value = false
    nextToken.value = first?.ContinuationToken
  },
  { immediate: true },
)

const items = computed(() => [...(page.value?.Items ?? []), ...more.value])

async function loadMore() {
  const scope = context.scope
  const token = nextToken.value
  if (!scope || !token) {
    return
  }
  const started = generation
  loadingMore.value = true
  try {
    const next = await getViewInbox(scope.org, scope.project, scope.view, {
      state: filter.value,
      continuationToken: token,
    })
    if (started === generation) {
      more.value = [...more.value, ...(next.Items ?? [])]
      nextToken.value = next.ContinuationToken
    }
  } catch (error) {
    if (started === generation) {
      toast.add({ title: 'The next page did not load', description: errorMessage(error), color: 'error' })
    }
  } finally {
    if (started === generation) {
      loadingMore.value = false
    }
  }
}

const refreshing = ref(false)

/** Both lists again: the ones that are shown at once, the other when its tab is opened. */
async function refresh() {
  if (!context.scope) {
    return
  }
  refreshing.value = true
  try {
    await invalidateInbox(queryClient, context.scope)
  } finally {
    refreshing.value = false
  }
}

/** The ids of the items whose accept or dismiss is on its way: their rows take no second one. */
const busy = ref(new Set<string>())
/** The ids of the jobs whose archives are being fetched: their rows take no second Download, the other rows do. */
const downloads = ref(new Set<string>())
/** The item whose import result is shown. */
const resultOf = ref<ViewInboxItem>()

// Another view: its items are other ones, and the report of the view before is not theirs. The fetched archives stay:
// each fetch removes its own id when it ends, so its row is still busy when the person comes back to the view.
watch(
  () => context.scope,
  () => {
    busy.value = new Set()
    resultOf.value = undefined
  },
)

/**
 * A refused accept or dismiss, by what the API answered ("Routes" of inbox.md): somebody closed the item meanwhile
 * (409 InboxItemClosed), it expired or is above the caller's role (404), nothing runs its action in this deployment
 * (501), and anything else with the API's own message: a notification that is not an action, a job that could not
 * start, an item that kept changing.
 */
function reportRefusal(error: unknown, title: string) {
  if (hasErrorCode(error, 'InboxItemClosed')) {
    toast.add({ title: 'The item was already handled', description: errorMessage(error), color: 'warning' })
  } else if (isNotFound(error)) {
    toast.add({
      title: 'The item is gone',
      description: 'It has expired, or your role no longer sees it.',
      color: 'warning',
    })
  } else if (isStorytellerError(error) && error.status === 501) {
    toast.add({ title: 'This action is not available in this deployment', color: 'warning' })
  } else {
    toast.add({ title: writeFailureTitle(error, title), description: errorMessage(error), color: 'error' })
  }
}

/**
 * Accepts or dismisses an item with a plain call of the SDK, and reads both lists again after every answer: the item
 * is under Done, somebody else closed it, or it is still open. `refused` is the title of a refusal the API explains
 * itself.
 */
async function close(item: ViewInboxItem, run: (scope: Scope, id: string) => Promise<unknown>, refused: string) {
  const scope = context.scope
  const id = item.Id
  if (!scope || !id || busy.value.has(id)) {
    return
  }

  busy.value = new Set(busy.value).add(id)
  try {
    await run(scope, id)
  } catch (error) {
    reportRefusal(error, refused)
  }

  try {
    await invalidateInbox(queryClient, scope)
  } finally {
    busy.value = new Set([...busy.value].filter((other) => other !== id))
  }
}

async function accept(item: ViewInboxItem) {
  if (!item.Id || busy.value.has(item.Id) || !(await confirm(inboxAcceptConfirmation(item)))) {
    return
  }

  await close(
    item,
    async (scope, id) => {
      await acceptViewInboxItem(scope.org, scope.project, scope.view, id)
      toast.add({
        title: 'Action accepted',
        description: 'Its work runs in the background; the result arrives in this inbox.',
        color: 'success',
        icon: 'i-lucide-check',
      })
    },
    'The action could not start',
  )
}

const dismiss = (item: ViewInboxItem) =>
  close(item, (scope, id) => dismissViewInboxItem(scope.org, scope.project, scope.view, id), 'The item was not dismissed')

/** The archive of the item's export, through the route of its job and under the job's file name. */
async function download(item: ViewInboxItem) {
  const scope = context.scope
  const job = item.Job
  if (!scope || !job?.Id || downloads.value.has(job.Id)) {
    return
  }

  const id = job.Id
  downloads.value = new Set(downloads.value).add(id)
  try {
    await transfers.downloadArchive(scope, id, job.FileName)
  } catch (error) {
    toast.add(
      isNotFound(error)
        ? {
            title: 'The archive has expired',
            description: 'An export keeps its archive for a limited time. Export the view again.',
            color: 'warning',
            icon: 'i-lucide-clock',
          }
        : { title: 'The archive was not downloaded', description: errorMessage(error), color: 'error' },
    )
  } finally {
    downloads.value = new Set([...downloads.value].filter((other) => other !== id))
  }
}
</script>

<template>
  <UDashboardPanel id="inbox">
    <template #header>
      <AppNavbar title="Inbox">
        <template #right>
          <UTooltip v-if="canUseInbox" text="Refresh">
            <UButton
              icon="i-lucide-refresh-cw"
              color="neutral"
              variant="ghost"
              aria-label="Refresh"
              :loading="refreshing"
              @click="refresh"
            />
          </UTooltip>
        </template>
      </AppNavbar>
      <UDashboardToolbar v-if="!locked">
        <UTabs v-model="tab" :items="tabs" :content="false" variant="link" size="sm" />
      </UDashboardToolbar>
    </template>
    <template #body>
      <EmptyState
        v-if="locked"
        icon="i-lucide-lock"
        title="Contributors only"
        description="The inbox of the view, with the results of background jobs and the actions the platform suggests, is available to the project's Contributors."
      />
      <LoadingState v-else-if="isPending" label="Loading the inbox…" />
      <ErrorState v-else-if="failure" :error="failure" @retry="shown.refetch()" />
      <EmptyState
        v-else-if="items.length === 0"
        :icon="empty.icon"
        :title="empty.title"
        :description="empty.description"
      />
      <div v-else class="mx-auto w-full max-w-5xl">
        <!-- ph-no-capture: the items name the people who caused and closed them -->
        <ul
          class="ph-no-capture divide-y divide-default rounded-md border border-default"
          :aria-label="filter === 'Done' ? 'Done items' : 'Open items'"
        >
          <li v-for="(item, index) in items" :key="item.Id ?? index">
            <InboxItemRow
              :item="item"
              :busy="!!item.Id && busy.has(item.Id)"
              :downloading="!!item.Job?.Id && downloads.has(item.Job.Id)"
              @accept="accept(item)"
              @dismiss="dismiss(item)"
              @download="download(item)"
              @result="resultOf = item"
            />
          </li>
        </ul>
        <div v-if="nextToken" class="flex justify-center p-4">
          <UButton label="Load more" color="neutral" variant="outline" :loading="loadingMore" @click="loadMore" />
        </div>
      </div>

      <InboxImportResult
        v-if="context.scope"
        :scope="context.scope"
        :item="resultOf"
        @close="resultOf = undefined"
      />
    </template>
  </UDashboardPanel>
</template>
