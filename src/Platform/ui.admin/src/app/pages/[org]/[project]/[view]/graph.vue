<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'

// The annotation graph. Like the grid, the selected node's detail is a nested route (<RouterView>), so the graph
// keeps its layout, zoom and focus while the detail changes (spec section 12).
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const detail = useDetailPanel()

watch(
  () => context.scope,
  (scope) => scope && void catalog.load(scope),
  { immediate: true },
)

const selectedKey = computed(() => {
  const key = (route.params as { key?: string }).key
  return typeof key === 'string' ? key : undefined
})
const graphPath = computed(() => `/${context.org}/${context.project}/${context.view}/graph`)

function open(key: string) {
  void router.push({ path: `${graphPath.value}/${key}`, query: route.query })
}

function close() {
  void router.push({ path: graphPath.value, query: route.query })
}
</script>

<template>
  <UDashboardPanel
    id="graph"
    :class="{ hidden: detail.covers.value }"
    :ui="{ body: 'p-0 sm:p-0 gap-0 overflow-hidden' }"
  >
    <template #header>
      <AppNavbar title="Graph">
        <template #right>
          <UTooltip text="Reload the catalog">
            <UButton
              icon="i-lucide-refresh-cw"
              color="neutral"
              variant="ghost"
              aria-label="Reload the catalog"
              :loading="catalog.status === 'loading'"
              @click="context.scope && catalog.load(context.scope, { force: true })"
            />
          </UTooltip>
        </template>
      </AppNavbar>
    </template>

    <template #body>
      <AnnotationGraph :selected-key="selectedKey" class="min-h-0 flex-1" @open="open" @close="close" />
    </template>
  </UDashboardPanel>

  <RouterView />
</template>
