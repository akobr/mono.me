<script setup lang="ts">
/* eslint-disable vue/no-v-html -- renderInlineMarkdown escapes all HTML first and only emits http(s) links */
import { computed } from 'vue'
import { renderInlineMarkdown } from '@/utils/markdown'

/** Descriptions and schema help in a small, safe Markdown subset. */
const props = withDefaults(defineProps<{ source?: string | null; empty?: string }>(), {
  source: undefined,
  empty: undefined,
})

const html = computed(() => renderInlineMarkdown(props.source))
</script>

<template>
  <div
    v-if="html"
    class="space-y-2 [&_code]:rounded [&_code]:bg-elevated [&_code]:px-1 [&_code]:font-mono"
    v-html="html"
  />
  <p v-else-if="empty" class="text-dimmed">{{ empty }}</p>
</template>
