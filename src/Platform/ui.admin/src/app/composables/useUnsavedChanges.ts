import { onBeforeUnmount, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, type RouteLocationNormalized } from 'vue-router'
import { useEventListener } from '@vueuse/core'

const dirtySources = new Set<() => boolean>()

/** True when any mounted editor or dialog has unsaved changes (the step-up's warning, 424-264). */
export function hasUnsavedChanges(): boolean {
  return [...dirtySources].some((isDirty) => isDirty())
}

/**
 * Counts what a dialog holds (typed or pasted input) as unsaved changes while `dirty` is true, so the step-up's
 * confirmation warns that its sign-in loses them (424-264). Unlike useUnsavedChanges, no route guard and no browser
 * prompt: closing the dialog discards its input, as its Cancel does.
 */
export function useDirtySource(dirty: MaybeRefOrGetter<boolean>): void {
  const isDirty = () => toValue(dirty)
  dirtySources.add(isDirty)
  onBeforeUnmount(() => dirtySources.delete(isDirty))
}

let leaving = false

/**
 * The page is left on purpose, after the person was told the unsaved changes are lost (the step-up's sign-in,
 * 424-264): the browser's own prompt does not ask again. False asks again, for a sign-in that did not leave the page.
 */
export function leaveWithoutPrompt(value = true): void {
  leaving = value
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
  useDirtySource(isDirty)

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
    if (isDirty() && !leaving) {
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
