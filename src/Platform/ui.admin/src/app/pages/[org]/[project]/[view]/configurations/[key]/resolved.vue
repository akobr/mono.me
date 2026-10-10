<script setup lang="ts">
defineProps<{ annotationKey: string }>()
</script>

<template>
  <ConfigurationResolved :annotation-key="annotationKey" />
</template>
