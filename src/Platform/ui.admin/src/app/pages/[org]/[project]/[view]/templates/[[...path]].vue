<script setup lang="ts">
import { movedToModeling } from '@/utils/modeling'

// Templates moved to the Templates tab of Modeling (424-38); saved links, also to a template, keep working.
definePage({ redirect: (to) => movedToModeling(to.path) })
</script>

<template>
  <span hidden />
</template>
