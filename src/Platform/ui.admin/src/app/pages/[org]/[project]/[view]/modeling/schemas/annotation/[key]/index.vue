<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { SchemaTarget } from '@/utils/schema-targets'

const route = useRoute()
const target = computed<SchemaTarget>(() => ({
  kind: 'annotation',
  key: String((route.params as { key: string }).key),
}))
</script>

<template>
  <SchemaDetailPanel :target="target" />
</template>
