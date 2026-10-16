import { nextTick, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { useRoute } from 'vue-router'

/**
 * Scrolls the section the route's hash names into view (424-35): `#invitations` on Members, `#shared-certificates`
 * on Machines and `#secrets` on Settings, opened by links and by the redirects of the pages they replaced. The router
 * does not scroll to anchors, and the scroll container is the panel body, not the window. The section (focusable with
 * `tabindex="-1"`) also gets the focus, so keyboard and screen-reader users continue from it, as after a followed
 * anchor.
 *
 * `ready` turns true once the content above the section has loaded, so the section does not move after the scroll.
 * A refetch does not scroll again; another hash does.
 */
export function useHashScroll(ready: MaybeRefOrGetter<boolean> = true): void {
  const route = useRoute()
  let scrolled: string | undefined

  watch(
    () => [route.hash, toValue(ready)] as const,
    async ([hash, isReady]) => {
      if (!hash) {
        scrolled = undefined
        return
      }
      if (!isReady || hash === scrolled) {
        return
      }
      scrolled = hash
      await nextTick()
      // route.hash is decoded already.
      const section = document.getElementById(hash.slice(1))
      section?.scrollIntoView({ block: 'start' })
      section?.focus({ preventScroll: true })
    },
    { immediate: true },
  )
}
