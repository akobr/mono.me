<script setup>
import { onMounted, onUnmounted, ref } from 'vue'

// A Mermaid diagram of a ```mermaid fence (config.mjs), rendered in the browser. Mermaid is loaded on demand, only on
// pages with a diagram, and the diagram is drawn again when the site switches between light and dark.
const props = defineProps({
  id: { type: String, required: true },
  graph: { type: String, required: true },
})

const svg = ref('')
let observer
let renders = 0

async function draw() {
  const { default: mermaid } = await import('mermaid')
  const dark = document.documentElement.classList.contains('dark')
  mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: dark ? 'dark' : 'default' })
  // A new id per render: mermaid refuses an id that is still in the document.
  const result = await mermaid.render(`${props.id}-${++renders}`, decodeURIComponent(props.graph))
  svg.value = result.svg
}

onMounted(async () => {
  observer = new MutationObserver(() => draw())
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
  await draw()
})

onUnmounted(() => observer?.disconnect())
</script>

<template>
  <!-- The class "mermaid" is what public/tooltips.js looks for. -->
  <div class="mermaid" v-html="svg"></div>
</template>
