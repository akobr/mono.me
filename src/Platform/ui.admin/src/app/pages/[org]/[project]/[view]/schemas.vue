<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useGetConfigurationSchemas } from '@42for.net/2splatform.sdk/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { metaOf, typeCodes, typeMeta } from '@/utils/annotation-key'
import { schemaRoute, summaryTarget } from '@/utils/schema-targets'

// Type schemas (7 rows), annotation schemas and descendant-type schemas of the view (companion spec E7). Each
// row links to its editor (nested route) and, where it belongs to one, to the annotation.
const route = useRoute()
const context = useContextStore()
const catalog = useCatalogStore()
const scope = useScope()
const canEdit = useCan('edit')
const detail = useDetailPanel()
const creating = ref(false)

watch(
  () => context.scope,
  (value) => value && void catalog.load(value),
  { immediate: true },
)

const schemas = useGetConfigurationSchemas(scope.org, scope.project, scope.view, {
  query: { enabled: () => !!scope.view() },
})

const sections = computed(() => {
  const summaries = schemas.data.value ?? []
  const typeRows = typeCodes.map((code) => {
    const summary = summaries.find((entry) => entry.Kind === 'Type' && metaOf(entry.AnnotationType)?.code === code)
    return {
      id: `type:${code}`,
      code,
      title: typeMeta[code].label,
      key: undefined as string | undefined,
      summary,
      to: schemaRoute(context.scope!, { kind: 'type', type: code }),
    }
  })
  const rowsOf = (kind: 'Annotation' | 'DescendantType') =>
    summaries
      .filter((entry) => entry.Kind === kind)
      .map((summary) => {
        const target = summaryTarget(summary)!
        const code = metaOf(summary.AnnotationType)?.code ?? metaOf(summary.AnnotationKey?.slice(0, 3))?.code
        return {
          id: `${kind}:${summary.AnnotationKey}:${summary.AnnotationType ?? ''}`,
          code,
          title:
            kind === 'Annotation'
              ? summary.AnnotationKey!
              : `${metaOf(summary.AnnotationType)?.label ?? summary.AnnotationType} below`,
          key: summary.AnnotationKey,
          summary,
          to: schemaRoute(context.scope!, target),
        }
      })
  return [
    { id: 'type', title: 'Type schemas', rows: typeRows },
    { id: 'annotation', title: 'Annotation schemas', rows: rowsOf('Annotation') },
    { id: 'descendant', title: 'Descendant-type schemas', rows: rowsOf('DescendantType') },
  ]
})

const current = computed(() => route.path)
</script>

<template>
  <UDashboardPanel id="schemas" :class="{ hidden: detail.covers.value }">
    <template #header>
      <AppNavbar title="Schemas">
        <template #right>
          <UButton v-if="canEdit" label="New schema" icon="i-lucide-plus" @click="creating = true" />
        </template>
      </AppNavbar>
    </template>
    <template #body>
      <LoadingState v-if="schemas.isPending.value" />
      <ErrorState v-else-if="schemas.isError.value" :error="schemas.error.value" @retry="schemas.refetch()" />
      <div v-else class="space-y-6">
        <section v-for="section in sections" :key="section.id" class="space-y-2">
          <h2 class="text-xs font-semibold text-muted uppercase">{{ section.title }}</h2>
          <p v-if="section.rows.length === 0" class="text-sm text-muted">None.</p>
          <ul v-else class="divide-y divide-default rounded-md border border-default">
            <li
              v-for="row in section.rows"
              :key="row.id"
              class="flex flex-wrap items-center gap-3 px-4 py-2.5"
              :class="current === row.to ? 'bg-primary/5' : ''"
            >
              <AnnotationTypeBadge v-if="row.code" :type="row.code" />
              <RouterLink
                :to="row.to"
                class="min-w-0 truncate text-sm font-medium hover:underline"
                :class="row.key && section.id === 'annotation' ? 'font-mono text-xs' : ''"
              >
                {{ row.title }}
              </RouterLink>
              <RouterLink
                v-if="row.key && section.id === 'descendant'"
                :to="`/${context.org}/${context.project}/${context.view}/annotations/${row.key}`"
                class="font-mono text-xs text-primary hover:underline"
              >
                {{ row.key }}
              </RouterLink>
              <span v-if="row.summary" class="font-mono text-xs">v{{ row.summary.Version }}</span>
              <UBadge v-else label="No schema" color="neutral" variant="outline" size="sm" />
              <AuditActorLink v-if="row.summary?.Modified" :actor="row.summary.Modified.By" subtle class="text-xs" />
              <RelativeTime
                v-if="row.summary?.Modified"
                :value="row.summary.Modified.At"
                class="ms-auto text-xs text-dimmed"
              />
            </li>
          </ul>
        </section>
      </div>
      <SchemaCreateDialog v-model:open="creating" />
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
