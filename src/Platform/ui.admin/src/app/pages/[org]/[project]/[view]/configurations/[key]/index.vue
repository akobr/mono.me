<script setup lang="ts">
defineProps<{ annotationKey: string }>()
</script>

<template>
  <ConfigurationEditor :key="annotationKey" :annotation-key="annotationKey" />
</template>
