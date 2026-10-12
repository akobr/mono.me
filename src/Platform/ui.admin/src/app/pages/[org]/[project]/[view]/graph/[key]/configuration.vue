<script setup lang="ts">
// The configuration tab moved to the configuration detail (spec 4.1); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string; view: string; key: string }
    const base = `/${params.org}/${params.project}/${params.view}/configurations/${params.key}`
    // ?cfg= of the old sub-tabs picks the same tab of the configuration detail
    const tab = to.query.cfg === 'effective' || to.query.cfg === 'resolved' ? `/${to.query.cfg}` : ''
    return { path: `${base}${tab}`, query: { q: to.query.q } }
  },
})
</script>

<template>
  <span hidden />
</template>
