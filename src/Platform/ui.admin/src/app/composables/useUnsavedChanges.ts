import { onBeforeUnmount, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, type RouteLocationNormalized } from 'vue-router'
import { useEventListener } from '@vueuse/core'

const dirtySources = new Set<() => boolean>()

/** True when any mounted editor has unsaved changes (for the tab title dot). */
export function hasUnsavedChanges(): boolean {
  return [...dirtySources].some((isDirty) => isDirty())
}

/**
 * Guards unsaved edits (spec section 11.1): leaving the route or changing its params (another row) asks
 * "Discard changes?", and closing the browser tab triggers the native beforeunload prompt.
 */
export function useUnsavedChanges(
  dirty: MaybeRefOrGetter<boolean>,
  options: { message?: string; ignore?: (to: RouteLocationNormalized, from: RouteLocationNormalized) => boolean } = {},
) {
  const confirm = useConfirm()
  const isDirty = () => toValue(dirty)
  dirtySources.add(isDirty)
  onBeforeUnmount(() => dirtySources.delete(isDirty))

  // The navigations the user chose Discard for: the location first asked for, which is `to.redirectedFrom` behind a
  // route-record redirect (the plain Modeling URL) and `to` itself otherwise. A redirect, such as the page memory's,
  // runs this guard again with that location as its redirectedFrom: the user is asked once per navigation (424-67).
  const discarded = new WeakSet<object>()

  const guard = async (to: RouteLocationNormalized, from: RouteLocationNormalized) => {
    if (!isDirty() || options.ignore?.(to, from) || to.path === from.path) {
      return true
    }
    if (to.redirectedFrom && discarded.has(to.redirectedFrom)) {
      return true
    }

    const discard = await confirm({
      title: 'Discard changes?',
      description: options.message ?? 'Your changes have not been saved.',
      confirmLabel: 'Discard',
      cancelLabel: 'Keep editing',
      color: 'warning',
    })
    if (discard) {
      discarded.add(to.redirectedFrom ?? to)
    }
    return discard
  }

  onBeforeRouteLeave(guard)
  onBeforeRouteUpdate(guard)

  useEventListener(window, 'beforeunload', (event: BeforeUnloadEvent) => {
    if (isDirty()) {
      event.preventDefault()
    }
  })

  // The document title shows a dot while there are unsaved changes.
  watch(
    () => isDirty(),
    (value) => {
      document.title = value ? `● ${document.title.replace(/^● /, '')}` : document.title.replace(/^● /, '')
    },
    { immediate: true },
  )
  onBeforeUnmount(() => (document.title = document.title.replace(/^● /, '')))
}
