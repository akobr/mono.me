<script setup lang="ts">
import type { Annotation } from '@42for.net/2splatform.sdk'
// The same tab as in the annotation grid's detail.
import Tab from '../../annotations/[key]/configuration.vue'

defineProps<{ annotation: Annotation; fresh: boolean }>()
</script>

<template>
  <Tab :annotation="annotation" :fresh="fresh" />
</template>
