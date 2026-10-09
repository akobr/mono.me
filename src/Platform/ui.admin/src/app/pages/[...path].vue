<script setup lang="ts">
import { useRoute } from 'vue-router'

const route = useRoute()
</script>

<template>
  <UDashboardPanel id="not-found">
    <template #header>
      <AppNavbar title="Not found" />
    </template>
    <template #body>
      <EmptyState icon="i-lucide-map-pin-off" title="Page not found" :description="`Nothing lives at ${route.path}.`">
        <UButton to="/" label="Go home" icon="i-lucide-house" />
      </EmptyState>
    </template>
  </UDashboardPanel>
</template>
