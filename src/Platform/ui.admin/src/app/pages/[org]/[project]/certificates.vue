<script setup lang="ts">
// The shared certificates moved to the Machines page (424-35); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string }
    return { path: `/${params.org}/${params.project}/machines`, hash: '#shared-certificates' }
  },
})
</script>

<template>
  <span hidden />
</template>
