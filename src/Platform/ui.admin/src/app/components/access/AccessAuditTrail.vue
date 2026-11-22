<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { refDebounced } from '@vueuse/core'
import { getAccessAudit, type AccessAuditEntry, type GetAccessAuditParams } from '@42for.net/2splatform.sdk'
import { useAccessAudit } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'
import {
  accessAuditActionLabel,
  accessAuditActions,
  accessAuditEntryLabel,
  accessAuditProject,
  accessAuditTargetIcon,
  accessAuditTargetKind,
} from '@/utils/access-audit'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'

// The access audit trail of the organization (424-263, docs/Platform/Storyteller/access-audit.md): who changed members,
// access points, machines, policies, trusted issuers and shared certificates of the organization and its projects.
// Modelled on the secret audit of Monitor (monitor/AuditTrail.vue): filters in a toolbar, a table with a sticky header
// that scrolls itself, and Load more with the continuation token. Asked only for the organization's Administrators.
const context = useContextStore()
const session = useSessionStore()
const toast = useToast()
const canAdmin = useCan('manageAccess', 'org')

// The sentinels stand for every project or action: a Select item must not have an empty-string value, and '*' is in no
// project name, so a project called "all" stays selectable.
const allProjects = '*'
const allActions = '*'
const project = ref(allProjects)
const action = ref(allActions)
const target = ref('')
// the request follows the typing after a pause, not every key
const targetFilter = refDebounced(target, 300)
const from = ref('')
const to = ref('')

const params = computed<GetAccessAuditParams>(() => ({
  project: project.value === allProjects ? undefined : project.value,
  action: action.value === allActions ? undefined : action.value,
  target: targetFilter.value.trim() || undefined,
  from: from.value ? new Date(from.value).toISOString() : undefined,
  // the end date is inclusive, and so is the API's to: the last millisecond of the day
  to: to.value ? new Date(new Date(to.value).getTime() + 86_399_999).toISOString() : undefined,
}))

const filtered = computed(() => Object.values(params.value).some((value) => value !== undefined))

const first = useAccessAudit(params, canAdmin)
const more = ref<AccessAuditEntry[]>([])
const nextToken = ref<string | null>()
const loadingMore = ref(false)
// A page of more records belongs to the organization and filters it was asked for: changing them starts a new
// generation, and a page that arrives for an older one is dropped.
let generation = 0

watch([params, () => context.org], () => {
  generation++
  more.value = []
  nextToken.value = undefined
})

watch(
  () => first.data.value,
  (page) => {
    // a new first page (a refetch after a change) starts over: a page of more still loading followed the old one
    generation++
    more.value = []
    nextToken.value = page?.ContinuationToken
  },
  { immediate: true },
)

const records = computed(() => [...(first.data.value?.Items ?? []), ...more.value])

async function loadMore() {
  if (!context.org || !nextToken.value) {
    return
  }
  const started = generation
  loadingMore.value = true
  try {
    const page = await getAccessAudit(context.org, { ...params.value, continuationToken: nextToken.value })
    if (started === generation) {
      more.value = [...more.value, ...(page.Items ?? [])]
      nextToken.value = page.ContinuationToken
    }
  } catch (error) {
    if (started === generation) {
      toast.add({ title: 'The next page did not load', description: errorMessage(error), color: 'error' })
    }
  } finally {
    loadingMore.value = false
  }
}

// The projects of the filter: the caller's projects in the organization and every project of the records shown so
// far, which an Administrator of the organization need not be a member of.
const seenProjects = ref(new Set<string>())
watch(
  () => context.org,
  () => {
    seenProjects.value = new Set()
  },
)
watch(records, (list) => {
  const missing = list
    .map((entry) => accessAuditProject(entry.AccessPointKey))
    .filter((name): name is string => !!name && !seenProjects.value.has(name))
  if (missing.length) {
    seenProjects.value = new Set([...seenProjects.value, ...missing])
  }
})
const projectItems = computed(() => {
  const prefix = `${context.org}.`
  const names = new Set(seenProjects.value)
  for (const key of Object.keys(session.accessMap)) {
    if (key.startsWith(prefix)) {
      names.add(key.slice(prefix.length))
    }
  }
  if (project.value !== allProjects) {
    names.add(project.value)
  }
  return [
    { label: 'Every project', value: allProjects },
    ...[...names].toSorted().map((name) => ({ label: name, value: name })),
  ]
})

const actionItems = [
  { label: 'Every action', value: allActions },
  ...accessAuditActions.map((entry) => ({ label: accessAuditActionLabel(entry), value: entry })),
]
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <UDashboardToolbar class="py-2">
      <div class="flex w-full flex-wrap items-center gap-2">
        <USelect v-model="project" :items="projectItems" class="w-44" aria-label="Project filter" />
        <USelect v-model="action" :items="actionItems" class="w-60" aria-label="Action filter" />
        <UInput v-model="target" placeholder="Target id" class="w-48 font-mono" aria-label="Target filter" />
        <UInput v-model="from" type="date" class="w-40" aria-label="From" />
        <UInput v-model="to" type="date" class="w-40" aria-label="To" />
        <span class="ms-auto text-xs text-muted">{{ records.length }} record(s)</span>
      </div>
    </UDashboardToolbar>
    <LoadingState v-if="first.isPending.value" />
    <ErrorState v-else-if="first.isError.value" :error="first.error.value" @retry="first.refetch()" />
    <EmptyState
      v-else-if="records.length === 0"
      icon="i-lucide-scroll-text"
      :title="filtered ? 'No matching records' : 'No audit records'"
      description="Changes of members, projects, machines, trusted issuers, shared certificates and policies of the organization are recorded here and kept for two years."
    />
    <div v-else class="min-h-0 flex-1 overflow-auto">
      <!-- ph-no-capture: the targets and actors are people's names -->
      <table class="ph-no-capture w-full text-sm">
        <thead class="sticky top-0 z-10 bg-elevated/80 text-xs text-muted backdrop-blur">
          <tr>
            <th class="px-3 py-2 text-start font-medium">Time</th>
            <th class="px-3 py-2 text-start font-medium">Action</th>
            <th class="px-3 py-2 text-start font-medium">Project</th>
            <th class="px-3 py-2 text-start font-medium">Target</th>
            <th class="px-3 py-2 text-start font-medium">Change</th>
            <th class="px-3 py-2 text-start font-medium">Actor</th>
            <th class="px-3 py-2 text-start font-medium">Correlation</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-default">
          <tr v-for="entry in records" :key="entry.Id">
            <td class="px-3 py-2 whitespace-nowrap">{{ entry.Time ? formatDateTime(new Date(entry.Time)) : '' }}</td>
            <td class="px-3 py-2">
              <!-- a long detail (the rules of a new machine) takes two lines; the change shows it in full -->
              <div class="max-w-52 min-w-36">
                <span class="block font-medium">{{ accessAuditEntryLabel(entry) }}</span>
                <span v-if="entry.Detail" class="line-clamp-2 text-xs wrap-anywhere text-muted" :title="entry.Detail">
                  {{ entry.Detail }}
                </span>
              </div>
            </td>
            <td class="px-3 py-2">
              <span v-if="accessAuditProject(entry.AccessPointKey)" class="font-mono text-xs">
                {{ accessAuditProject(entry.AccessPointKey) }}
              </span>
              <span v-else class="text-xs text-muted">Organization</span>
            </td>
            <td class="px-3 py-2">
              <div v-if="entry.Target" class="flex max-w-48 items-start gap-1.5">
                <span :title="accessAuditTargetKind(entry.Target.Kind)" class="mt-0.5 flex shrink-0">
                  <UIcon
                    :name="accessAuditTargetIcon(entry.Target.Kind)"
                    class="size-4 text-muted"
                    aria-hidden="true"
                  />
                </span>
                <span class="sr-only">{{ accessAuditTargetKind(entry.Target.Kind) }}</span>
                <div class="min-w-0">
                  <span
                    v-if="entry.Target.Name"
                    class="block truncate font-medium text-highlighted"
                    :title="entry.Target.Name"
                  >
                    {{ entry.Target.Name }}
                  </span>
                  <span
                    class="block truncate font-mono text-xs"
                    :class="entry.Target.Name ? 'text-muted' : 'text-highlighted'"
                    :title="entry.Target.Id"
                  >
                    {{ entry.Target.Id }}
                  </span>
                </div>
              </div>
            </td>
            <td class="px-3 py-2"><AccessAuditChange :entry="entry" /></td>
            <td class="px-3 py-2 text-xs whitespace-nowrap">
              <AuditActorLink :actor="entry.Actor" />
              <span v-if="entry.Actor?.Via" class="text-muted"> via {{ entry.Actor.Via }}</span>
              <!-- the client app the person used with the actor: an opaque id or a URL, so a long one may wrap -->
              <span v-if="entry.Actor?.Via && entry.Actor.ViaClient" class="whitespace-normal wrap-anywhere text-muted">
                (client {{ entry.Actor.ViaClient }})</span
              >
            </td>
            <td class="px-3 py-2">
              <span v-if="entry.CorrelationId" class="inline-flex items-center gap-1 font-mono text-xs text-muted">
                {{ entry.CorrelationId.slice(0, 8) }}…
                <CopyButton :value="entry.CorrelationId" />
              </span>
            </td>
          </tr>
        </tbody>
      </table>
      <div v-if="nextToken" class="flex justify-center p-4">
        <UButton label="Load more" color="neutral" variant="outline" :loading="loadingMore" @click="loadMore" />
      </div>
    </div>
  </div>
</template>
