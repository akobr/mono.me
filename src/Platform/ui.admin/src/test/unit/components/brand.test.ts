import { describe, expect, it } from 'vitest'
import { h } from 'vue'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import logo from '@/assets/images/42logo-128.png'
import SidebarBrand from '@/components/shell/SidebarBrand.vue'
import BareLayout from '@/layouts/BareLayout.vue'
import { mountInApp, settle } from '../../helpers'

const createTestRouter = () =>
  createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
  })

async function mountBrand(collapsed: boolean) {
  const view = mountInApp(SidebarBrand, { collapsed }, [createTestRouter()])
  await settle()
  return view
}

// The navigation bar and the bare pages show the 42 logo, not the "2S" placeholder in public/favicon.svg (424-32).
describe('brand', () => {
  it('shows the 42 logo and the name in the navigation bar', async () => {
    const view = await mountBrand(false)
    const brand = view.inner()

    const image = brand.get('img')
    expect(image.attributes('src')).toBe(logo)
    expect(image.attributes('alt')).toBe('')
    expect(brand.get('a').attributes('aria-label')).toBe('2S Admin home')
    expect(brand.get('a').text()).toBe('2S Admin')
    view.host.unmount()
  })

  it('keeps the 42 logo on the icon rail', async () => {
    const view = await mountBrand(true)
    const brand = view.inner()

    expect(brand.get('img').attributes('src')).toBe(logo)
    expect(brand.get('a').text()).toBe('')
    view.host.unmount()
  })

  it('shows the 42 logo above the card of the bare pages', () => {
    const layout = mount(BareLayout, { slots: { default: () => h('p', 'Sign in') } })

    expect(layout.get('img').attributes('src')).toBe(logo)
    expect(layout.get('img').attributes('alt')).toBe('')
    expect(layout.text()).toContain('2S Admin')
    expect(layout.text()).toContain('Sign in')
    layout.unmount()
  })
})
