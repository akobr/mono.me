import { nextTick, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'

/**
 * Keeps the scroll offset of a list in `offset`: once the element exists and `ready()` (the rows are there), the
 * remembered offset is restored; after that every scroll writes it back. Nothing is written before the restore, so
 * a list that is still loading cannot overwrite the memory.
 */
export function useScrollMemory(
  element: MaybeRefOrGetter<HTMLElement | null | undefined>,
  offset: Ref<number>,
  ready: () => boolean,
): void {
  let restored = false

  watch(
    [() => toValue(element), ready],
    async ([target, isReady]) => {
      if (restored || !target || !isReady) {
        return
      }
      restored = true
      const top = offset.value
      await nextTick()
      target.scrollTop = top
      // A virtualized list may size its content a frame later.
      if (Math.abs(target.scrollTop - top) > 1) {
        requestAnimationFrame(() => (target.scrollTop = top))
      }
    },
    { immediate: true, flush: 'post' },
  )

  useEventListener(
    element,
    'scroll',
    (event: Event) => {
      if (restored) {
        offset.value = (event.target as HTMLElement).scrollTop
      }
    },
    { passive: true },
  )
}
