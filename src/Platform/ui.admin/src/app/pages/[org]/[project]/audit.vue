<script setup lang="ts">
// The audit trail moved to the Audit tab of Monitor (424-35); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string }
    return { path: `/${params.org}/${params.project}/monitor`, query: { ...to.query, tab: 'audit' } }
  },
})
</script>

<template>
  <span hidden />
</template>
