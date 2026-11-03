import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useHashScroll } from '@/composables/useHashScroll'
import { settle } from '../../helpers'

const scrolls: string[] = []
const mounted: VueWrapper[] = []
const scrollIntoView = vi.fn(function (this: HTMLElement) {
  scrolls.push(this.id)
})

async function mountPage(path: string, ready = ref(true), shown = ref(false)) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
  })
  await router.push(path)
  await router.isReady()

  const Page = defineComponent({
    setup() {
      useHashScroll(ready)
      // The section exists only once the content has loaded, as on the merged pages.
      return () =>
        h('div', [
          h('section', { id: 'members', tabindex: -1 }),
          ready.value ? h('section', { id: 'invitations', tabindex: -1 }) : null,
          h('section', { id: '100%', tabindex: -1 }),
          // A section of a role, rendered only once the role allows it.
          shown.value ? h('section', { id: 'trusted-issuers', tabindex: -1 }) : null,
        ])
    },
  })
  const wrapper = mount(Page, { attachTo: document.body, global: { plugins: [router] } })
  mounted.push(wrapper)
  await settle()
  return { wrapper, router, ready }
}

const original = HTMLElement.prototype.scrollIntoView
beforeEach(() => {
  HTMLElement.prototype.scrollIntoView = scrollIntoView
})

afterEach(() => {
  mounted.splice(0).forEach((wrapper) => wrapper.unmount())
  HTMLElement.prototype.scrollIntoView = original
  scrolls.length = 0
  scrollIntoView.mockClear()
})

describe('hash scroll', () => {
  it('scrolls to the section of the hash and moves the focus to it', async () => {
    await mountPage('/acme/billing/members#invitations')

    expect(scrolls).toEqual(['invitations'])
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'start' })
    expect(document.activeElement?.id).toBe('invitations')
  })

  it('looks the hash up as the router decoded it', async () => {
    await mountPage('/acme/billing/members#100%25')

    expect(scrolls).toEqual(['100%'])
  })

  it('waits until the content above the section has loaded', async () => {
    const ready = ref(false)
    await mountPage('/acme/billing/members#invitations', ready)
    expect(scrolls).toEqual([])

    ready.value = true
    await settle()
    expect(scrolls).toEqual(['invitations'])
  })

  it('keeps the hash of a section that is not rendered, and scrolls to it once it appears', async () => {
    const ready = ref(true)
    const shown = ref(false)
    await mountPage('/acme/billing/machines#trusted-issuers', ready, shown)
    expect(scrolls).toEqual([])

    // A role granted since shows the section, once the content above it has loaded again (424-164).
    ready.value = false
    shown.value = true
    await settle()
    ready.value = true
    await settle()
    expect(scrolls).toEqual(['trusted-issuers'])
    expect(document.activeElement?.id).toBe('trusted-issuers')

    ready.value = false
    await settle()
    ready.value = true
    await settle()
    expect(scrolls).toEqual(['trusted-issuers'])
  })

  it('does not scroll without a hash, or again when the content only reloads', async () => {
    const ready = ref(true)
    const { router } = await mountPage('/acme/billing/members', ready)
    expect(scrolls).toEqual([])

    await router.push('/acme/billing/members#invitations')
    await settle()
    ready.value = false
    await settle()
    ready.value = true
    await settle()
    expect(scrolls).toEqual(['invitations'])

    await router.push('/acme/billing/members#members')
    await settle()
    expect(scrolls).toEqual(['invitations', 'members'])
  })
})
