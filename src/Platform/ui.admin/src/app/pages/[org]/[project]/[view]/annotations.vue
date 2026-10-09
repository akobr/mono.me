<script setup lang="ts">
import { useContextStore } from '@/stores/context'

const context = useContextStore()
</script>

<template>
  <UDashboardPanel id="annotations">
    <template #header>
      <AppNavbar title="Annotations" />
    </template>
    <template #body>
      <EmptyState
        icon="i-lucide-tags"
        title="Annotations"
        :description="`The annotation catalog of ${context.org} / ${context.project} / ${context.view} is part of the next phase.`"
      />
    </template>
  </UDashboardPanel>
</template>
