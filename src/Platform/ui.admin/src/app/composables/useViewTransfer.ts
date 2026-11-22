import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useContextStore } from '@/stores/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { compareLocation, fitsInAddress, viewTransferLimit } from '@/utils/view-compare'
import type { ViewItemKind } from '@/utils/view-documents'

/**
 * The one place that knows the rules of comparing a selection with another view of the project (spec "Copy and
 * Compare Annotations and Configurations Between Views", section 8): the bulk bars of Annotations and Configurations
 * and the More actions menus of both details take their button and their menu item from here. `keys` are the selected
 * keys, or the one key of a detail.
 */
export function useViewTransfer(kind: ViewItemKind, keys: MaybeRefOrGetter<readonly string[]>) {
  const context = useContextStore()
  const router = useRouter()
  const store = useViewTransferStore()
  const others = useOtherViews()

  /** Why the actions are off: no other view, or more keys than the limit. Undefined when they are on. */
  const blocked = computed(() => {
    // Only once the views are known: while they load, or when they failed, the comparison itself says what it lacks.
    if (others.alone.value) {
      return 'The project has no other view'
    }
    return toValue(keys).length > viewTransferLimit
      ? `Select up to ${viewTransferLimit} to compare or copy them at once`
      : undefined
  })

  /** The plain address of the comparison, when the keys fit in an address. */
  const compareTo = computed<RouteLocationRaw | undefined>(() => {
    const scope = context.scope
    const selected = toValue(keys)
    return scope && !blocked.value && fitsInAddress(selected)
      ? compareLocation(scope, { kind, keys: selected })
      : undefined
  })

  /** Opens the comparison; keeps a long list of keys first. */
  async function openCompare(): Promise<void> {
    const scope = context.scope
    if (blocked.value || !scope) {
      return
    }
    const selected = toValue(keys)
    // A list is kept when the comparison is opened, never while its address is computed: every change of a long
    // selection would otherwise keep a list, and push the lists of the comparisons that are open out of the store.
    const set = fitsInAddress(selected) ? undefined : store.remember(selected)
    await router.push(compareLocation(scope, { kind, keys: selected, set }))
  }

  /** "Compare with view" as the items of a menu group. A blocked item stays, disabled, with the reason below it. */
  const items = computed<DropdownMenuItem[]>(() => {
    const item: DropdownMenuItem = { label: 'Compare with view', icon: 'i-lucide-git-compare' }
    if (blocked.value) {
      return [{ ...item, disabled: true, description: blocked.value }]
    }
    // A link where the keys fit in an address: it can be opened in a new tab, and a form with unsaved changes asks
    // before it is left. A longer list has no address until it is kept, so its item opens the comparison itself.
    return [compareTo.value ? { ...item, to: compareTo.value } : { ...item, onSelect: () => void openCompare() }]
  })

  return { blocked, compareTo, openCompare, items }
}
