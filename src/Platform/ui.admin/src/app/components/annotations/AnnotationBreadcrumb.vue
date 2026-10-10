<script setup lang="ts">
import { computed } from 'vue'
import type { LocationQuery } from 'vue-router'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { metaOf, parseAnnotationKey } from '@/utils/annotation-key'
import { parentKey, type TreePerspective } from '@/utils/tree-projection'

/**
 * The chain of parents in the current tree perspective, built from the key segments (spec section 11.1), for
 * example "Subject northwind › Context prod › Execution prod". Ancestors missing from the catalog are struck through.
 */
const props = withDefaults(
  defineProps<{
    annotationKey: string
    perspective: TreePerspective
    query?: LocationQuery
    /** The screen the links stay on. */
    section?: 'annotations' | 'graph'
  }>(),
  { query: undefined, section: 'annotations' },
)

const catalog = useCatalogStore()
const context = useContextStore()

const chain = computed(() => {
  const keys: string[] = []
  let current: string | undefined = props.annotationKey
  while (current) {
    keys.unshift(current)
    const parsed = parseAnnotationKey(current)
    current = parsed ? parentKey(parsed, props.perspective) : undefined
  }

  return keys.map((key) => ({
    key,
    meta: metaOf(key.slice(0, 3)),
    name: key.split('.').at(-1),
    missing: !catalog.byKey.has(key) && catalog.status === 'ready',
    to: { path: `/${context.org}/${context.project}/${context.view}/${props.section}/${key}`, query: props.query },
  }))
})
</script>

<template>
  <nav aria-label="Annotation ancestors" class="flex flex-wrap items-center gap-1 text-xs">
    <template v-for="(item, index) in chain" :key="item.key">
      <UIcon v-if="index > 0" name="i-lucide-chevron-right" class="size-3 text-dimmed" />
      <span
        v-if="index === chain.length - 1"
        class="flex items-center gap-1 font-medium text-highlighted"
        aria-current="page"
      >
        <UIcon v-if="item.meta" :name="item.meta.icon" :class="item.meta.textClass" class="size-3.5" />
        {{ item.meta?.label }} {{ item.name }}
      </span>
      <span
        v-else-if="item.missing"
        class="flex items-center gap-1 text-dimmed line-through"
        :title="`${item.key} is missing`"
      >
        {{ item.meta?.label }} {{ item.name }}
      </span>
      <RouterLink
        v-else
        :to="item.to"
        class="flex items-center gap-1 text-muted hover:text-highlighted hover:underline"
      >
        <UIcon v-if="item.meta" :name="item.meta.icon" :class="item.meta.textClass" class="size-3.5" />
        {{ item.meta?.label }} {{ item.name }}
      </RouterLink>
    </template>
  </nav>
</template>
