<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isNotFound } from '@42for.net/2splatform.sdk'
import { useAnnotation } from '@/queries/scope'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'

/**
 * The annotation detail beside the grid or the graph (spec section 11.1). The detail always works on a fresh
 * GetAnnotation, never on the catalog snapshot; the tabs are child routes rendered by the inner <RouterView>.
 */
const props = withDefaults(defineProps<{ section?: 'annotations' | 'graph' }>(), { section: 'annotations' })

const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const preferences = usePreferencesStore()

const key = computed(() => String((route.params as { key: string }).key))
const query = useAnnotation(key)
const annotation = computed(() => query.data.value ?? catalog.byKey.get(key.value))

watch(key, (value) => preferences.rememberKey(value), { immediate: true })

function close() {
  void router.push({ path: `/${context.org}/${context.project}/${context.view}/${props.section}`, query: route.query })
}
</script>

<template>
  <DetailPanel :id="`${section}-detail`" :title="annotation?.Name ?? key" @close="close">
    <template #header="{ close: closePanel }">
      <AnnotationDetail :annotation-key="key" :annotation="annotation" :section="section" @close="closePanel" />
    </template>

    <LoadingState v-if="query.isPending.value && !annotation" />
    <EmptyState
      v-else-if="query.isError.value && isNotFound(query.error.value)"
      icon="i-lucide-search-x"
      title="Annotation not found"
      :description="`${key} does not exist in this view. It may have been deleted.`"
    />
    <ErrorState v-else-if="query.isError.value" :error="query.error.value" @retry="query.refetch()" />
    <RouterView v-else-if="annotation" v-slot="{ Component }">
      <component :is="Component" :annotation="annotation" :fresh="!!query.data.value" />
    </RouterView>
  </DetailPanel>
</template>
