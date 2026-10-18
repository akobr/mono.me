<script setup lang="ts">
import { movedToModeling } from '@/utils/modeling'

// Export & Import moved to the Export & Import tab of Modeling (424-38); saved links keep working.
definePage({ redirect: (to) => movedToModeling(to.path) })
</script>

<template>
  <span hidden />
</template>
