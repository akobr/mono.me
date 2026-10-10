<script setup lang="ts">
import type { Annotation } from '@42for.net/2splatform.sdk'

defineProps<{ annotation: Annotation; fresh: boolean }>()
</script>

<template>
  <AnnotationForm :key="annotation.AnnotationKey" :annotation="annotation" :fresh="fresh" />
</template>
