import { ref, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'

const open = ref(false)

// Between lg and xl the docked sidebar starts as the icon rail (spec section 15), and crossing xl switches it; "[" still
// toggles it. This is module state, so the layout keeps what the user set when it comes back after a bare route (424-168).
const wide = useMediaQuery('(min-width: 1280px)')
const collapsed = ref(!wide.value)
watch(wide, (value) => (collapsed.value = !value))

/**
 * Navigation state shared by the navbar's hamburger, the sidebar and the "[" shortcut:
 * - `open`: the slideover (below lg with a pinned sidebar, or at every size when the sidebar is unpinned);
 * - `collapsed`: the docked sidebar reduced to its icon rail.
 */
export function useSidebar() {
  const desktop = useMediaQuery('(min-width: 1024px)')

  return {
    open,
    collapsed,
    toggle: () => (open.value = !open.value),
    close: () => (open.value = false),
    /** "[": collapse the docked sidebar on desktop, otherwise open or close the slideover. */
    toggleFor: (pinned: boolean) => {
      if (pinned && desktop.value) {
        collapsed.value = !collapsed.value
      } else {
        open.value = !open.value
      }
    },
  }
}
