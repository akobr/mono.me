<script setup lang="ts">
/* eslint-disable vue/no-v-html -- renderMarkdown escapes raw HTML and only emits http(s), mailto and relative URLs */
import { computed } from 'vue'
import { renderMarkdown } from '@/utils/markdown'

/** Descriptions and schema help rendered as Markdown (headings, lists, quotes, code blocks, tables, links). */
const props = withDefaults(defineProps<{ source?: string | null; empty?: string }>(), {
  source: undefined,
  empty: undefined,
})

const html = computed(() => renderMarkdown(props.source))
</script>

<template>
  <div v-if="html" class="markdown" v-html="html" />
  <p v-else-if="empty" class="text-dimmed">{{ empty }}</p>
</template>

<style scoped>
/* The rendered HTML has no scope attribute, and Tailwind's preflight removes the defaults of lists, headings and tables. */
.markdown {
  overflow-wrap: break-word;
}

.markdown > :deep(* + *),
.markdown :deep(:is(blockquote, li) > * + *) {
  margin-top: 0.5rem;
}

.markdown :deep(:is(h1, h2, h3, h4, h5, h6)) {
  font-weight: 600;
  line-height: 1.3;
  color: var(--ui-text-highlighted);
}

.markdown > :deep(:is(h1, h2, h3, h4, h5, h6):not(:first-child)) {
  margin-top: 1em;
}

.markdown :deep(h1) {
  font-size: 1.25em;
}

.markdown :deep(h2) {
  font-size: 1.125em;
}

.markdown :deep(h3) {
  font-size: 1em;
}

.markdown :deep(:is(h4, h5, h6)) {
  font-size: 0.875em;
}

.markdown :deep(ul) {
  list-style: disc;
  padding-left: 1.5em;
}

.markdown :deep(ul ul) {
  list-style: circle;
}

.markdown :deep(ol) {
  list-style: decimal;
  padding-left: 1.5em;
}

.markdown :deep(li + li) {
  margin-top: 0.25rem;
}

.markdown :deep(blockquote) {
  border-left: 0.25rem solid var(--ui-border-accented);
  padding-left: 0.75em;
  color: var(--ui-text-muted);
}

.markdown :deep(code) {
  border-radius: 0.25rem;
  background: var(--ui-bg-elevated);
  padding: 0 0.25rem;
  font-family: var(--font-mono);
}

.markdown :deep(pre) {
  overflow-x: auto;
  border: 1px solid var(--ui-border);
  border-radius: 0.375rem;
  background: var(--ui-bg-elevated);
  padding: 0.5rem 0.75rem;
}

.markdown :deep(pre code) {
  border-radius: 0;
  background: none;
  padding: 0;
}

.markdown :deep(.markdown-table) {
  overflow-x: auto;
}

.markdown :deep(table) {
  border-collapse: collapse;
}

.markdown :deep(:is(th, td)) {
  border: 1px solid var(--ui-border);
  padding: 0.25rem 0.5rem;
  text-align: left;
}

.markdown :deep(th) {
  background: var(--ui-bg-elevated);
  font-weight: 600;
  color: var(--ui-text-highlighted);
}

.markdown :deep(hr) {
  border-color: var(--ui-border);
}

.markdown :deep(a) {
  color: var(--ui-primary);
  text-decoration: underline;
}
</style>
