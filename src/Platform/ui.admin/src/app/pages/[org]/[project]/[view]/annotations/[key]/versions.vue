<script setup lang="ts">
import type { Annotation } from '@42for.net/2splatform.sdk'

defineProps<{ annotation: Annotation; fresh: boolean }>()
</script>

<template>
  <ConfigurationVersions :annotation-key="annotation.AnnotationKey" />
</template>
