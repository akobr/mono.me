<script setup lang="ts">
import type { Annotation } from '@42for.net/2splatform.sdk'

// The annotation detail's History tab (spec 4.3).
defineProps<{ annotation: Annotation; fresh: boolean }>()
</script>

<template>
  <AnnotationHistory :annotation-key="annotation.AnnotationKey" />
</template>
