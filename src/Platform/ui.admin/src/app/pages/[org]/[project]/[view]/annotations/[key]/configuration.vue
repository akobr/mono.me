<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { Annotation } from '@42for.net/2splatform.sdk'

// The annotation detail's Configuration tab: the configuration screens embedded in the panel (spec section 11.1),
// with Edit, Effective and Resolved as sub-tabs (?cfg=).
const props = defineProps<{ annotation: Annotation; fresh: boolean }>()

const route = useRoute()
const router = useRouter()

const tab = computed({
  get: () => (route.query.cfg === 'effective' || route.query.cfg === 'resolved' ? route.query.cfg : 'edit'),
  set: (value: string) => void router.replace({ query: { ...route.query, cfg: value === 'edit' ? undefined : value } }),
})
const tabs = [
  { label: 'Edit', value: 'edit', icon: 'i-lucide-pencil' },
  { label: 'Effective', value: 'effective', icon: 'i-lucide-layers' },
  { label: 'Resolved', value: 'resolved', icon: 'i-lucide-function-square' },
]
const key = computed(() => props.annotation.AnnotationKey)
</script>

<template>
  <div class="flex min-h-full flex-col">
    <div class="px-4 pt-3">
      <UTabs v-model="tab" :items="tabs" :content="false" size="xs" variant="link" />
    </div>
    <!-- The editor stays mounted (v-show): a sub-tab is only a query change, which skips the unsaved-changes guard. -->
    <ConfigurationEditor v-show="tab === 'edit'" :key="key" :annotation-key="key" class="flex-1" />
    <ConfigurationEffective v-if="tab === 'effective'" :annotation-key="key" />
    <ConfigurationResolved v-if="tab === 'resolved'" :annotation-key="key" />
  </div>
</template>
