<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { metaOf } from '@/utils/annotation-key'
import type { SchemaTarget } from '@/utils/schema-targets'

const route = useRoute()
const params = computed(() => route.params as { key: string; type: string })
const target = computed<SchemaTarget | undefined>(() => {
  const code = metaOf(params.value.type)?.code
  return code ? { kind: 'descendant', key: params.value.key, type: code } : undefined
})
</script>

<template>
  <SchemaDetailPanel v-if="target" :target="target" />
</template>
