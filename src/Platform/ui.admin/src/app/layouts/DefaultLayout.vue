<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { useContextStore } from '@/stores/context'
import { useSessionStore } from '@/stores/session'

const { access } = storeToRefs(useContextStore())
const session = useSessionStore()

interface StoredSidebar {
  size?: number
  collapsed?: boolean
}

// UDashboardSidebar shows its stored state, not the start useSidebar sets for the width of the window. A stored rail
// stays; a stored "expanded" is read as no choice, so the start decides (424-168). It is written back as it was, so a
// start over it stores nothing, and a later start at another width finds it. An open window reads only at its start:
// a read after another window's write would drop "expanded" there too, and undo what that window shows.
const sidebarStorage = {
  listenToStorageChanges: false,
  serializer: {
    read: (raw: string): StoredSidebar => {
      const stored: StoredSidebar = JSON.parse(raw)
      return stored.collapsed ? stored : { size: stored.size }
    },
    write: (state: StoredSidebar) => JSON.stringify({ ...state, collapsed: state.collapsed ?? false }),
  },
}
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
