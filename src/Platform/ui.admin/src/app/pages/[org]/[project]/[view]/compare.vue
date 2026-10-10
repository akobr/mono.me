<script setup lang="ts">
import { computed, defineAsyncComponent, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { getConfiguration, getConfigurationViewDiff, isNotFound, type DiffResult } from '@42for.net/2splatform.sdk'
import { useGetViews } from '@42for.net/2splatform.sdk/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { toJson } from '@/utils/json-yaml'

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
const target = computed({
  get: () => text(route.query.to),
  set: (value) => void router.replace({ query: { ...route.query, to: value } }),
})

const keyItems = computed(() => {
  const configured = catalog.configurations ? [...catalog.configurations.keys()] : [...catalog.byKey.keys()]
  return configured.toSorted()
})
const viewItems = computed(() =>
  (views.data.value ?? []).map((view) => view.Name!).filter((name) => name && name !== context.view),
)

const ready = computed(() => !!key.value && !!target.value && !!context.scope)

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
      <AppNavbar title="Compare views" />
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
          v-if="!ready"
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
