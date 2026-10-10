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

  const guard = async (to: RouteLocationNormalized, from: RouteLocationNormalized) => {
    if (!isDirty() || options.ignore?.(to, from) || to.path === from.path) {
      return true
    }

    return confirm({
      title: 'Discard changes?',
      description: options.message ?? 'Your changes have not been saved.',
      confirmLabel: 'Discard',
      cancelLabel: 'Keep editing',
      color: 'warning',
    })
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
