<script setup lang="ts">
// The invitations of a project moved to its Members page (424-35); saved links keep working.
definePage({
  redirect: (to) => {
    const params = to.params as { org: string; project: string }
    return { path: `/${params.org}/${params.project}/members`, hash: '#invitations' }
  },
})
</script>

<template>
  <span hidden />
</template>
