<script setup lang="ts">
import { onMounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'
import { markSidebarStarted, sidebarStorage } from '@/composables/useSidebar'

const { access } = storeToRefs(useContextStore())
const session = useSessionStore()

onMounted(markSidebarStarted)

</script>

<template>
  <UDashboardGroup storage="local" storage-key="2s-admin" unit="%" :storage-options="sidebarStorage">
    <AppSidebar />

    <UDashboardPanel v-if="session.accountStatus === 'error'" id="account-error">
      <template #header>
        <AppNavbar title="Account" />
      </template>
      <template #body>
        <ErrorState
          :error="session.accountError"
          title="Your account could not be loaded"
          @retry="session.loadAccount(true)"
        />
      </template>
    </UDashboardPanel>

    <UDashboardPanel v-else-if="access === 'forbidden'" id="forbidden">
      <template #header>
        <AppNavbar title="No access" />
      </template>
      <template #body>
        <EmptyState
          icon="i-lucide-shield-x"
          title="You have no access here"
          description="Your account is not a member of this organization or project. Ask an administrator to invite you."
        >
          <UButton to="/orgs" label="Your organizations" icon="i-lucide-building-2" />
        </EmptyState>
      </template>
    </UDashboardPanel>

    <slot v-else />

    <CommandPalette />
    <TransferToaster />
  </UDashboardGroup>
</template>
