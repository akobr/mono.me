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
 * A refetch does not scroll again; another hash does. A hash whose section is not rendered stays pending: the next time
 * `ready` turns true, the section is looked up again, so one that appears later (a role granted since, 424-164) still
 * gets the scroll and the focus.
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
      await nextTick()
      // Another hash came meanwhile, or another run scrolled to this one.
      if (hash !== route.hash || hash === scrolled) {
        return
      }
      // route.hash is decoded already.
      const section = document.getElementById(hash.slice(1))
      if (!section) {
        return
      }
      scrolled = hash
      section.scrollIntoView({ block: 'start' })
      section.focus({ preventScroll: true })
    },
    { immediate: true },
  )
}
