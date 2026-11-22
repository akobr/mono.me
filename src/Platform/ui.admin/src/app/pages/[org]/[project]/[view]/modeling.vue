<script setup lang="ts">
// Modeling (424-38): the schemas, templates, export and import, deleted annotations and comparison of views of a view,
// a tab each (modeling/{tab}). Every tab page brings its own panel and detail outlet, which must stay children of the
// dashboard group, so this page is only the outlet, without an element around it. Every member of the project opens
// it: below Contributor the tabs are read-only and hide their create, edit, delete and restore actions (424-272).
// Export & Import needs the Administrator role, and its page has a lock of its own (424-66).
</script>

<template>
  <RouterView />
</template>
