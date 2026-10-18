<script setup lang="ts">
import { movedToModeling } from '@/utils/modeling'

// Schemas moved to the Schemas tab of Modeling (424-38); saved links, also to a schema, keep working.
definePage({ redirect: (to) => movedToModeling(to.path) })
</script>

<template>
  <span hidden />
</template>
