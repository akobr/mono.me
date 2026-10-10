<script setup lang="ts">
import { computed } from 'vue'
import { statusMeta, type AnnotationStatus } from '@/utils/annotation-status'

const props = withDefaults(defineProps<{ status: AnnotationStatus; size?: 'xs' | 'sm' | 'md' }>(), { size: 'sm' })
const meta = computed(() => statusMeta[props.status])
</script>

<template>
  <UBadge :label="meta.label" :icon="meta.icon" :color="meta.color" variant="subtle" :size="size" />
</template>
