<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useKeyValue } from '@/queries/scope'
import { useContextStore } from '@/stores/context'

// The detail of a pair (spec 29.3): Value and Versions as child routes beside the list.
const route = useRoute()
const router = useRouter()
const context = useContextStore()

const key = computed(() => String((route.params as { key: string }).key))
const pair = useKeyValue(key)
const base = computed(() => `/${context.org}/${context.project}/${context.view}/key-values/${key.value}`)
const query = computed(() => ({ q: route.query.q, state: route.query.state, level: route.query.level }))

const tabs = computed(() => [
  { label: 'Value', icon: 'i-lucide-key-round', to: { path: base.value, query: query.value }, exact: true },
  { label: 'Versions', icon: 'i-lucide-history', to: { path: `${base.value}/versions`, query: query.value } },
])

function close() {
  void router.push({ path: `/${context.org}/${context.project}/${context.view}/key-values`, query: query.value })
}
</script>

<template>
  <DetailPanel id="key-value-detail" :title="key" @close="close">
    <template #header="{ close: closePanel }">
      <div class="flex flex-col gap-2 border-b border-default px-4 py-3">
        <div class="flex items-start gap-2">
          <UButton icon="i-lucide-arrow-left" color="neutral" variant="ghost" size="sm" aria-label="Back to the list" class="md:hidden" @click="closePanel" />
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <h2 class="truncate font-mono text-base font-semibold text-highlighted">{{ key }}</h2>
              <CopyButton :value="key" />
              <KeyValueLevelBadge v-if="pair.data.value" :level="pair.data.value.Protection" :reference="pair.data.value.IsReference" />
              <UBadge v-if="pair.data.value?.IsDeleted" label="Deleted" icon="i-lucide-trash-2" color="warning" variant="subtle" size="sm" />
            </div>
          </div>
          <UButton icon="i-lucide-x" color="neutral" variant="ghost" size="sm" aria-label="Close the detail" class="hidden md:inline-flex" @click="closePanel" />
        </div>
        <UNavigationMenu :items="tabs" highlight class="-mb-3" :ui="{ link: 'text-sm' }" />
      </div>
    </template>

    <RouterView v-slot="{ Component }">
      <component :is="Component" :key-name="key" />
    </RouterView>
  </DetailPanel>
</template>
