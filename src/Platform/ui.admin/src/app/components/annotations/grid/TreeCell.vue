<script setup lang="ts">
import type { TreeNode } from '@/utils/tree-projection'

/** Indentation, expand toggle and type badge of a tree row. */
defineProps<{ node: TreeNode; expanded: boolean }>()
const emit = defineEmits<{ toggle: [] }>()
</script>

<template>
  <div class="flex items-center gap-1" :style="{ paddingInlineStart: `${node.depth * 16}px` }">
    <UButton
      v-if="node.children.length"
      :icon="expanded ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'"
      :aria-label="expanded ? `Collapse ${node.key}` : `Expand ${node.key}`"
      :aria-expanded="expanded"
      color="neutral"
      variant="ghost"
      size="xs"
      square
      class="-my-1"
      @click.stop="emit('toggle')"
    />
    <span v-else class="inline-block w-6" />
    <AnnotationTypeBadge :type="node.key.slice(0, 3)" variant="label" :class="node.missing ? 'opacity-60' : ''" />
    <span v-if="node.children.length" class="text-xs text-dimmed">{{ node.children.length }}</span>
  </div>
</template>
