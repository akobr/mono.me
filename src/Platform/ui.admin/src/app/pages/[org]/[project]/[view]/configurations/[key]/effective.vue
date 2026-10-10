<script setup lang="ts">
defineProps<{ annotationKey: string }>()
</script>

<template>
  <ConfigurationEffective :annotation-key="annotationKey" />
</template>
