import { nextTick, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import { useEventListener, useResizeObserver } from '@vueuse/core'

/** False while the element or an ancestor is `display: none`, as the list is behind a full-screen detail. */
const isShown = (element: HTMLElement) => element.checkVisibility?.() ?? element.getClientRects().length > 0

/**
 * Keeps the scroll offset of a list in `offset`: once the element exists and `ready()` (the rows are there), the
 * remembered offset is restored; after that every scroll writes it back. Nothing is written before the restore, so
 * a list that is still loading cannot overwrite the memory.
 *
 * A detail that covers the list (full screen, and always on a phone) hides it with `display: none`, which drops its
 * scroll position. While the list is hidden its scrolls are not recorded, and when it shows again the remembered
 * offset is put back.
 */
export function useScrollMemory(
  element: MaybeRefOrGetter<HTMLElement | null | undefined>,
  offset: Ref<number>,
  ready: () => boolean,
): void {
  let restored = false
  let hidden = false

  async function restore(target: HTMLElement) {
    const top = offset.value
    await nextTick()
    target.scrollTop = top
    // A virtualized list may size its content a frame later.
    if (Math.abs(target.scrollTop - top) > 1) {
      requestAnimationFrame(() => (target.scrollTop = top))
    }
  }

  watch(
    [() => toValue(element), ready],
    async ([target, isReady]) => {
      if (restored || !target || !isReady) {
        return
      }
      restored = true
      if (!isShown(target)) {
        // Opened with the detail covering the list: the offset is put back when the list shows.
        hidden = true
        return
      }
      await restore(target)
    },
    { immediate: true, flush: 'post' },
  )

  // Hiding and showing change the size of the list, so the observer sees both.
  useResizeObserver(element, () => {
    const target = toValue(element)
    if (!target || !restored) {
      return
    }
    if (!isShown(target)) {
      hidden = true
    } else if (hidden) {
      hidden = false
      void restore(target)
    }
  })

  useEventListener(
    element,
    'scroll',
    (event: Event) => {
      const target = event.target as HTMLElement
      // The browser resets the offset of a hidden list, and reports it once the list shows again.
      if (restored && !hidden && isShown(target)) {
        offset.value = target.scrollTop
      }
    },
    { passive: true },
  )
}
