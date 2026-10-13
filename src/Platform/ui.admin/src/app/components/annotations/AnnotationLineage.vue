<script setup lang="ts">
import { computed } from 'vue'
import type { LocationQueryRaw } from 'vue-router'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { getDirectAncestors, metaOf } from '@/utils/annotation-key'

/**
 * Every ancestor as a small layered DAG in merge order (spec section 11.1): the level closest to the annotation
 * first, each level in merge order. Configurations inherit in this order.
 */
const props = withDefaults(
  defineProps<{
    annotationKey: string
    /** The screen the links stay on: the configuration detail links to the ancestors' configurations. */
    section?: 'annotations' | 'graph' | 'configurations'
    query?: LocationQueryRaw
  }>(),
  { section: 'annotations', query: undefined },
)

const catalog = useCatalogStore()
const context = useContextStore()

const levels = computed(() => {
  const result: string[][] = []
  const seen = new Set<string>()
  let current = [props.annotationKey]

  while (current.length) {
    const next = [...new Set(current.flatMap((key) => getDirectAncestors(key)))].filter((key) => !seen.has(key))
    next.forEach((key) => seen.add(key))
    if (next.length) {
      result.push(next)
    }
    current = next
  }

  return result.toReversed()
})

const link = (key: string) => ({
  path: `/${context.org}/${context.project}/${context.view}/${props.section}/${key}`,
  query: props.query,
})
</script>

<template>
  <UPopover :content="{ align: 'start' }">
    <UButton
      label="Lineage"
      icon="i-lucide-git-fork"
      color="neutral"
      variant="ghost"
      size="xs"
      :disabled="levels.length === 0"
    />
    <template #content>
      <div class="max-w-md space-y-2 p-3 text-xs">
        <p class="font-semibold text-highlighted">Ancestors, applied top to bottom</p>
        <ol class="space-y-2">
          <li v-for="(level, index) in levels" :key="index" class="flex flex-wrap items-center gap-1.5">
            <span class="w-4 text-dimmed">{{ index + 1 }}</span>
            <component
              :is="catalog.byKey.has(key) ? 'RouterLink' : 'span'"
              v-for="key in level"
              :key="key"
              :to="catalog.byKey.has(key) ? link(key) : undefined"
              class="inline-flex items-center gap-1 rounded border border-default px-1.5 py-0.5 font-mono"
              :class="catalog.byKey.has(key) ? 'hover:bg-elevated' : 'text-dimmed line-through'"
            >
              <UIcon
                :name="metaOf(key.slice(0, 3))?.icon ?? 'i-lucide-circle'"
                :class="metaOf(key.slice(0, 3))?.textClass"
                class="size-3"
              />
              {{ key }}
            </component>
          </li>
          <li class="flex items-center gap-1.5">
            <span class="w-4 text-dimmed">{{ levels.length + 1 }}</span>
            <span class="rounded bg-primary/10 px-1.5 py-0.5 font-mono font-medium">{{ annotationKey }}</span>
          </li>
        </ol>
      </div>
    </template>
  </UPopover>
</template>
