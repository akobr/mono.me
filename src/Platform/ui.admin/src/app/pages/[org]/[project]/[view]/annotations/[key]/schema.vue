<script setup lang="ts">
// The schema tab moved to the configuration detail (spec 4.1); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string; view: string; key: string }
    const base = `/${params.org}/${params.project}/${params.view}/configurations/${params.key}`
    return { path: `${base}/schema`, query: { q: to.query.q } }
  },
})
</script>

<template>
  <span hidden />
</template>
