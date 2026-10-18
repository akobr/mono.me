<script setup lang="ts">
// Modeling (424-38): the schemas, templates, export and import, deleted annotations and comparison of views of a view,
// a tab each (modeling/{tab}). Every tab page brings its own panel and detail outlet, which must stay children of the
// dashboard group, so for Contributors this page is only the outlet, without an element around it. Below Contributor
// the whole page is locked, and no tab is mounted.
const canEdit = useCan('edit')
</script>

<template>
  <UDashboardPanel v-if="!canEdit" id="modeling">
    <template #header>
      <AppNavbar title="Modeling" />
    </template>
    <template #body>
      <EmptyState
        icon="i-lucide-lock"
        title="Contributors only"
        description="The schemas, the templates, the export and import, the deleted annotations and the comparison of views are available to the project's Contributors."
      />
    </template>
  </UDashboardPanel>
  <RouterView v-else />
</template>
