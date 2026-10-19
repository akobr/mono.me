<script setup lang="ts">
import { computed, defineAsyncComponent, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { getConfiguration, getConfigurationViewDiff, isNotFound, type DiffResult } from '@42for.net/2splatform.sdk'
import { useGetViews } from '@42for.net/2splatform.sdk/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { toJson } from '@/utils/json-yaml'
import { hasOtherView } from '@/utils/modeling'

const DiffEditor = defineAsyncComponent(() => import('@/components/editor/DiffEditor.vue'))

// The same key across two views (spec sections 11.3 and 13): GetConfigurationViewDiff for the counts and hunks,
// both effective documents for the side-by-side view. Addressed by ?key=&to=.
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()

watch(
  () => context.scope,
  (scope) => scope && void catalog.load(scope),
  { immediate: true },
)

const views = useGetViews(
  () => context.org ?? '',
  () => context.project ?? '',
  undefined,
  { query: { enabled: () => !!context.project } },
)

const text = (value: unknown) => (typeof value === 'string' && value ? value : undefined)
const key = computed({
  get: () => text(route.query.key),
  set: (value) => void router.replace({ query: { ...route.query, key: value } }),
})

// The view of this page is the one of its route, which changes together with ?to=. The context guard puts the next
// org, project and view in the store before a navigation lands; until the store and the route agree again, nothing is
// preselected or requested, as a router.replace would cancel that navigation (424-64).
const page = computed(() => route.params as { org?: string; project?: string; view?: string })
const landed = computed(
  () => page.value.org === context.org && page.value.project === context.project && page.value.view === context.view,
)

// ?to= naming the view of this page is no other view: the Views page used to link the default view to itself
// (424-64). It counts as no target, so nothing is requested and the only other view is preselected over it.
const target = computed({
  get: () => {
    const to = text(route.query.to)
    return to === page.value.view ? undefined : to
  },
  set: (value) => void router.replace({ query: { ...route.query, to: value } }),
})

const keyItems = computed(() => {
  const configured = catalog.configurations ? [...catalog.configurations.keys()] : [...catalog.byKey.keys()]
  return configured.toSorted()
})
const viewItems = computed(() =>
  (views.data.value ?? []).map((view) => view.Name!).filter((name) => name && name !== context.view),
)

// With one other view there is nothing to pick: preselect it, unless ?to= already names a view (the Views page
// can link to a discovered view, which is not in this list). Once a navigation has landed, it looks again.
watch(
  [viewItems, target, landed],
  ([items, to, here]) => {
    if (here && !to && items.length === 1) {
      target.value = items[0]
    }
  },
  { immediate: true },
)

const ready = computed(() => landed.value && !!key.value && !!target.value && !!context.scope)

// Without another view the tab is disabled (ModelingHeader, the same test), but its address still opens the page: it
// says there is nothing to compare with instead of asking for another view (424-64).
const alone = computed(
  () => landed.value && !target.value && views.isSuccess.value && !hasOtherView(views.data.value, context.view),
)

const diff = useQuery({
  queryKey: computed(() => ['view-diff', context.org, context.project, context.view, key.value, target.value]),
  queryFn: ({ signal }) =>
    getConfigurationViewDiff(context.org!, context.project!, context.view!, key.value!, target.value!, undefined, {
      signal,
    }) as Promise<DiffResult>,
  enabled: ready,
  meta: { silent: true },
})

const documents = useQuery({
  queryKey: computed(() => ['view-pair', context.org, context.project, context.view, key.value, target.value]),
  queryFn: async ({ signal }) => {
    const load = async (view: string) => {
      try {
        return toJson(
          (await getConfiguration(context.org!, context.project!, view, key.value!, { signal })).Content ?? {},
        )
      } catch (error) {
        if (isNotFound(error)) {
          return '{}'
        }
        throw error
      }
    }
    const [current, other] = await Promise.all([load(context.view!), load(target.value!)])
    return { current, other }
  },
  enabled: ready,
})
</script>

<template>
  <UDashboardPanel id="compare">
    <template #header>
      <ModelingHeader />
    </template>
    <template #body>
      <div class="space-y-4">
        <div class="flex flex-wrap items-end gap-3">
          <UFormField label="Annotation key" class="min-w-72 flex-1">
            <USelectMenu
              v-model="key"
              :items="keyItems"
              placeholder="Pick a key"
              class="w-full"
              :ui="{ base: 'font-mono' }"
              virtualize
            />
          </UFormField>
          <UFormField :label="`Compare ${context.view} with`">
            <USelect v-model="target" :items="viewItems" placeholder="Another view" class="w-48" />
          </UFormField>
        </div>

        <EmptyState
          v-if="alone"
          icon="i-lucide-git-compare"
          title="No other view to compare with"
          description="The project has only this view. An Administrator can create another one on the Views page."
        />
        <EmptyState
          v-else-if="!ready"
          icon="i-lucide-git-compare"
          title="Compare a configuration across views"
          description="Pick a key and another view of this project to see how its effective configuration differs."
        />
        <LoadingState v-else-if="documents.isPending.value" />
        <ErrorState v-else-if="documents.isError.value" :error="documents.error.value" @retry="documents.refetch()" />
        <DiffEditor
          v-else
          :original="documents.data.value!.current"
          :modified="documents.data.value!.other"
          :stats="diff.data.value?.Stats"
          :hunks="diff.data.value?.Hunks"
          :original-label="context.view"
          :modified-label="target"
        />
      </div>
    </template>
  </UDashboardPanel>
</template>
