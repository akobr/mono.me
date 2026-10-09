<script setup lang="ts">
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string }
    return `/${params.org}/${params.project}/default/annotations`
  },
})
</script>

<template>
  <span hidden />
</template>
