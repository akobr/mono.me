<script setup lang="ts">
defineProps<{ keyName: string }>()
</script>

<template>
  <KeyValueDetail :key="keyName" :key-name="keyName" />
</template>
