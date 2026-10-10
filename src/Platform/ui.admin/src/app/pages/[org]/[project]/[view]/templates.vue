<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useGetTemplates } from '@42for.net/2splatform.sdk/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { metaOf, typeCodes, typeMeta } from '@/utils/annotation-key'

// The seven annotation types with their template (companion spec E8): presence, version, author, and how many
// configurations of the view use it. The detail is a nested route.
const route = useRoute()
const context = useContextStore()
const catalog = useCatalogStore()
const scope = useScope()

watch(
  () => context.scope,
  (value) => value && void catalog.load(value),
  { immediate: true },
)

const templates = useGetTemplates(scope.org, scope.project, scope.view, { query: { enabled: () => !!scope.view() } })

const rows = computed(() => {
  void catalog.revision
  const summaries = new Map(
    (templates.data.value ?? []).map((summary) => [metaOf(summary.AnnotationType)?.code, summary]),
  )
  const configured = catalog.configurations ? [...catalog.configurations.keys()] : []
  return typeCodes.map((code) => ({
    code,
    meta: typeMeta[code],
    summary: summaries.get(code),
    configurations: configured.filter((key) => key.startsWith(`${code}.`)).length,
  }))
})

const selected = computed(() => (route.params as { type?: string }).type)
</script>

<template>
  <UDashboardPanel id="templates">
    <template #header>
      <AppNavbar title="Templates" />
    </template>
    <template #body>
      <LoadingState v-if="templates.isPending.value" />
      <ErrorState v-else-if="templates.isError.value" :error="templates.error.value" @retry="templates.refetch()" />
      <ul v-else class="divide-y divide-default rounded-md border border-default">
        <li v-for="row in rows" :key="row.code">
          <RouterLink
            :to="`/${context.org}/${context.project}/${context.view}/templates/${row.code}`"
            class="flex flex-wrap items-center gap-3 px-4 py-3 hover:bg-elevated/50"
            :class="selected === row.code ? 'bg-primary/5' : ''"
          >
            <AnnotationTypeBadge :type="row.code" variant="label" />
            <span v-if="row.summary" class="font-mono text-xs">v{{ row.summary.Version }}</span>
            <UBadge v-else label="No template" color="neutral" variant="outline" size="sm" />
            <span v-if="row.summary?.Author" class="text-sm text-muted">{{ row.summary.Author }}</span>
            <RelativeTime v-if="row.summary?.UpdatedAt" :value="row.summary.UpdatedAt" class="text-xs text-dimmed" />
            <span class="ms-auto text-xs text-muted"
              >{{ row.configurations }} configuration{{ row.configurations === 1 ? '' : 's' }}</span
            >
          </RouterLink>
        </li>
      </ul>
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
