<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'

// The configuration detail (spec section 11.2): Edit, Effective, Resolved, Versions and Schema as child routes.
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()

const key = computed(() => String((route.params as { key: string }).key))
const annotation = computed(() => catalog.byKey.get(key.value))
const base = computed(() => `/${context.org}/${context.project}/${context.view}/configurations/${key.value}`)
const query = computed(() => ({ q: route.query.q }))

const tabs = computed(() => [
  { label: 'Edit', icon: 'i-lucide-pencil', to: { path: base.value, query: query.value }, exact: true },
  { label: 'Effective', icon: 'i-lucide-layers', to: { path: `${base.value}/effective`, query: query.value } },
  { label: 'Resolved', icon: 'i-lucide-function-square', to: { path: `${base.value}/resolved`, query: query.value } },
  { label: 'Versions', icon: 'i-lucide-history', to: { path: `${base.value}/versions`, query: query.value } },
  { label: 'Schema', icon: 'i-lucide-braces', to: { path: `${base.value}/schema`, query: query.value } },
])

function close() {
  void router.push({ path: `/${context.org}/${context.project}/${context.view}/configurations`, query: query.value })
}
</script>

<template>
  <DetailPanel id="configuration-detail" :title="key" @close="close">
    <template #header="{ close: closePanel }">
      <div class="flex flex-col gap-2 border-b border-default px-4 py-3">
        <div class="flex items-start gap-2">
          <UButton
            icon="i-lucide-arrow-left"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Back to the list"
            class="md:hidden"
            @click="closePanel"
          />
          <AnnotationTypeBadge :type="key.slice(0, 3)" variant="label" class="mt-0.5" />
          <div class="min-w-0 flex-1">
            <RouterLink
              :to="`/${context.org}/${context.project}/${context.view}/annotations/${key}`"
              class="flex items-center gap-1 text-xs text-muted hover:text-highlighted hover:underline"
            >
              Annotation <UIcon name="i-lucide-chevron-right" class="size-3" />
            </RouterLink>
            <h2 class="truncate text-base font-semibold text-highlighted">
              {{ annotation?.Name ?? key.split('.').at(-1) }}
            </h2>
            <KeyText :value="key" copy />
          </div>
          <DetailPanelActions />
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Close the detail"
            class="hidden md:inline-flex"
            @click="closePanel"
          />
        </div>
        <div class="flex">
          <AnnotationLineage :annotation-key="key" section="configurations" :query="query" />
        </div>
        <UNavigationMenu :items="tabs" highlight class="-mb-3" :ui="{ link: 'text-sm' }" />
      </div>
    </template>

    <RouterView v-slot="{ Component }">
      <component :is="Component" :annotation-key="key" />
    </RouterView>
  </DetailPanel>
</template>
