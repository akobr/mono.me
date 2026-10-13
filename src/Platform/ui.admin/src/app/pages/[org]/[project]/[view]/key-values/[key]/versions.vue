<script setup lang="ts">
defineProps<{ keyName: string }>()
</script>

<template>
  <KeyValueVersions :key="keyName" :key-name="keyName" />
</template>
