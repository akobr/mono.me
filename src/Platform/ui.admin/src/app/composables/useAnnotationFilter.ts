import { computed } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import { useNow } from '@vueuse/core'
import { useCatalogStore } from '@/stores/catalog'
import { compileFilter, parseFilter } from '@/utils/filter-parser'
import type { TreePerspective } from '@/utils/tree-projection'

export type ListMode = 'grid' | 'tree'

const first = (value: unknown) => (Array.isArray(value) ? value[0] : value)
const text = (value: unknown) => (typeof first(value) === 'string' ? (first(value) as string) : '')

/**
 * The filter state of the annotation screens, kept in the URL (spec section 5): `q` (filter), `mode`
 * (grid | tree) and `by` (subject | responsibility). The predicate runs over the whole catalog.
 */
export function useAnnotationFilter() {
  const route = useRoute()
  const router = useRouter()
  const catalog = useCatalogStore()
  const now = useNow({ interval: 60_000 })

  function update(changes: Record<string, string | undefined>): void {
    const query: LocationQueryRaw = { ...route.query }
    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined || value === '') {
        delete query[key]
      } else {
        query[key] = value
      }
    }
    void router.replace({ query })
  }

  const query = computed({
    get: () => text(route.query.q),
    set: (value: string) => update({ q: value.trim() || undefined }),
  })

  const mode = computed<ListMode>({
    get: () => (text(route.query.mode) === 'tree' ? 'tree' : 'grid'),
    set: (value) => update({ mode: value === 'tree' ? 'tree' : undefined }),
  })

  const perspective = computed<TreePerspective>({
    get: () => (text(route.query.by) === 'responsibility' ? 'responsibility' : 'subject'),
    set: (value) => update({ by: value === 'responsibility' ? 'responsibility' : undefined }),
  })

  const parsed = computed(() => parseFilter(query.value))

  const predicate = computed(() =>
    compileFilter(parsed.value, {
      now: now.value.getTime(),
      configured: catalog.configured,
      ancestorsOf: catalog.ancestorsOf,
      partsOf: catalog.partsOf,
    }),
  )

  const active = computed(() => parsed.value.terms.length > 0)
  const filtered = computed(() => (active.value ? catalog.annotations.filter(predicate.value) : catalog.annotations))

  return { query, mode, perspective, parsed, predicate, active, filtered, update }
}
