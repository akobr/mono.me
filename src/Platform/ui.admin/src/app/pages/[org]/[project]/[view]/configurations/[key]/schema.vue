<script setup lang="ts">
defineProps<{ annotationKey: string }>()
</script>

<template>
  <CombinedSchemaView :annotation-key="annotationKey" />
</template>
