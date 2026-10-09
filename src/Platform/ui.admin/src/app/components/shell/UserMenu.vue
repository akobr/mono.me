<script setup lang="ts">
import { computed } from 'vue'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const shortcuts = useShortcutsHelp()

const items = computed<DropdownMenuItem[][]>(() => [
  [
    {
      type: 'label',
      label: session.user?.name ?? 'Signed out',
      avatar: { src: session.user?.pictureUrl, alt: session.user?.name },
    },
  ],
  [
    { label: 'Account', icon: 'i-lucide-user-round', to: '/account' },
    { label: 'Organizations', icon: 'i-lucide-layout-grid', to: '/orgs' },
    { label: 'Keyboard shortcuts', icon: 'i-lucide-keyboard', kbds: ['?'], onSelect: () => shortcuts.show() },
  ],
  [{ label: 'Sign out', icon: 'i-lucide-log-out', onSelect: () => void session.signOut() }],
])
</script>

<template>
  <UDropdownMenu :items="items" :content="{ align: 'end' }" :ui="{ content: 'w-56' }">
    <UButton color="neutral" variant="ghost" square :aria-label="`User menu for ${session.user?.name ?? 'user'}`">
      <UAvatar :src="session.user?.pictureUrl" :alt="session.user?.name" size="xs" />
    </UButton>
  </UDropdownMenu>
</template>
