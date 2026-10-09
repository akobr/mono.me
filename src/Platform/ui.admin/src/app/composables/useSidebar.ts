import { ref } from 'vue'
import { useMediaQuery } from '@vueuse/core'

const open = ref(false)
const collapsed = ref(false)

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
