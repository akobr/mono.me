<script setup lang="ts">
// The secrets settings moved to the Secrets section of the project's Settings (424-35); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string }
    return { path: `/${params.org}/${params.project}/settings`, hash: '#secrets' }
  },
})
</script>

<template>
  <span hidden />
</template>
