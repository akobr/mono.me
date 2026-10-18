<script setup lang="ts">
import { movedToModeling } from '@/utils/modeling'

// Compare views moved to the Compare views tab of Modeling (424-38); saved links keep working.
definePage({ redirect: (to) => movedToModeling(to.path) })
</script>

<template>
  <span hidden />
</template>
