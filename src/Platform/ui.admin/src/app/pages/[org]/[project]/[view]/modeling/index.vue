<script setup lang="ts">
// Modeling opens on its first tab, Schemas (424-38); the query and the hash stay.
definePage({ redirect: (to) => `${to.path.replace(/\/$/, '')}/schemas` })
</script>

<template>
  <span hidden />
</template>
