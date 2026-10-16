<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

// The usage and the secret audit trail of the project's key values (spec 29.7) on one page (424-35): a tab each,
// kept in ?tab= (Usage without it). Only the open tab is mounted, so only its query runs. Administrators only.
const route = useRoute()
const router = useRouter()
const canAdmin = useCan('manageAccess')

const tabs = [
  { label: 'Usage', value: 'usage', icon: 'i-lucide-gauge' },
  { label: 'Audit', value: 'audit', icon: 'i-lucide-scroll-text' },
]
const tab = computed({
  get: () => (route.query.tab === 'audit' ? 'audit' : 'usage'),
  set: (value: string) =>
    void router.replace({ query: { ...route.query, tab: value === 'usage' ? undefined : value } }),
})
// The audit table fills the panel and scrolls itself, below its filter toolbar.
const fill = computed(() => canAdmin.value && tab.value === 'audit')
</script>

<template>
  <UDashboardPanel id="monitor" :ui="{ body: fill ? 'p-0 sm:p-0 gap-0 overflow-hidden' : undefined }">
    <template #header>
      <AppNavbar title="Monitor" />
      <UDashboardToolbar v-if="canAdmin">
        <UTabs v-model="tab" :items="tabs" :content="false" variant="link" size="sm" />
      </UDashboardToolbar>
    </template>
    <template #body>
      <EmptyState
        v-if="!canAdmin"
        icon="i-lucide-lock"
        title="Administrators only"
        description="The usage and the audit trail of the project's key values are visible to its Administrators."
      />
      <AuditTrail v-else-if="tab === 'audit'" />
      <UsageReport v-else />
    </template>
  </UDashboardPanel>
</template>
