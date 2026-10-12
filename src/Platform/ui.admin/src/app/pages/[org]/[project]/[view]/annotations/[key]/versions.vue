<script setup lang="ts">
// The versions tab moved to the configuration detail (spec 4.1); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string; view: string; key: string }
    const base = `/${params.org}/${params.project}/${params.view}/configurations/${params.key}`
    return { path: `${base}/versions`, query: { q: to.query.q, v: to.query.v, from: to.query.from } }
  },
})
</script>

<template>
  <span hidden />
</template>
