<script setup lang="ts">
defineProps<{ annotationKey: string }>()
</script>

<template>
  <ConfigurationVersions :annotation-key="annotationKey" />
</template>
