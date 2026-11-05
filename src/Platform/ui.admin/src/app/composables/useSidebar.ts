import { ref, watch } from 'vue'
import { useMediaQuery } from '@vueuse/core'

const open = ref(false)

// Between lg and xl the docked sidebar starts as the icon rail (spec section 15), and crossing xl switches it; "[" still
// toggles it. This is module state, so the layout keeps what the user set when it comes back after a bare route (424-168).
const wide = useMediaQuery('(min-width: 1280px)')
const collapsed = ref(!wide.value)
watch(wide, (value) => (collapsed.value = !value))

interface StoredSidebar {
  size?: number
  collapsed?: boolean
}

let started = false
let storedCollapsed = false

// UDashboardSidebar shows its stored state, not `collapsed`. Only the first read of a page may bring back a stored rail;
// a stored "expanded" is no choice, so the start decides (424-168). A later read (the layout after a bare route, a new
// pin) keeps `collapsed`, as the shared value may come from another window. What was not changed is written back as it
// was, so reading stores nothing. An open window does not listen: an event would be such a read, and undo its state.
// Below xl the rail comes from the width, so it is not stored as a choice for a later start at xl.
export const sidebarStorage = {
  listenToStorageChanges: false,
  serializer: {
    read: (raw: string): StoredSidebar => {
      const stored: StoredSidebar = JSON.parse(raw)
      storedCollapsed = stored.collapsed ?? false
      return !started && stored.collapsed ? stored : { size: stored.size }
    },
    write: (state: StoredSidebar) => {
      const choice = state.collapsed && !wide.value ? undefined : state.collapsed
      storedCollapsed = choice ?? storedCollapsed
      return JSON.stringify({ ...state, collapsed: storedCollapsed })
    },
  },
}

/** The layout has mounted, also with nothing stored: from now on `collapsed` decides over the stored state. */
export function markSidebarStarted(): void {
  started = true
}

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
