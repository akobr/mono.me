<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { metaOf } from '@/utils/annotation-key'
import type { SchemaTarget } from '@/utils/schema-targets'

const route = useRoute()
const code = computed(() => metaOf(String((route.params as { type: string }).type))?.code)
const target = computed<SchemaTarget | undefined>(() => (code.value ? { kind: 'type', type: code.value } : undefined))
</script>

<template>
  <SchemaDetailPanel v-if="target" :target="target" />
</template>
