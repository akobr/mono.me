<script setup lang="ts">
// The usage moved to the Usage tab of Monitor (424-35); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string }
    return `/${params.org}/${params.project}/monitor`
  },
})
</script>

<template>
  <span hidden />
</template>
