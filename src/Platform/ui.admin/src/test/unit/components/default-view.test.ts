import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import AppSidebar from '@/components/shell/AppSidebar.vue'
import ContextSwitcher from '@/components/shell/ContextSwitcher.vue'
import { useSidebar } from '@/composables/useSidebar'
import { mockHandlers } from '@/mock/all-handlers'
import { usePreferencesStore } from '@/stores/preferences'
import { mountScreen, settle } from '../../helpers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const scope = (view: string) => ({ org: 'acme', project: 'billing', view })

// The default view is the production one: a "prod" badge marks it in the switcher and in the sidebar (424-43).
describe('default view in the switcher', () => {
  async function mountSwitcher(view: string) {
    const mounted = await mountScreen(ContextSwitcher, {}, { scope: scope(view) })
    await settle()
    const buttons = mounted.inner().findAll('nav[aria-label="Context"] > button')
    return { ...mounted, viewButton: buttons[2]! }
  }

  it('shows prod between the name and the chevrons of the default view', async () => {
    const { host, viewButton } = await mountSwitcher('default')

    const label = viewButton.get('[data-slot="label"]').element
    const badge = label.nextElementSibling
    expect(label.textContent).toBe('default')
    expect(badge?.textContent).toBe('prod')
    expect(badge?.nextElementSibling?.getAttribute('data-slot')).toBe('trailingIcon')
    expect(viewButton.findAll('[data-slot="trailingIcon"]')).toHaveLength(1)
    expect(viewButton.text()).toBe('defaultprod')
    host.unmount()
  })

  it('shows no badge on another view', async () => {
    const { host, viewButton } = await mountSwitcher('staging')

    expect(viewButton.text()).toBe('staging')
    expect(viewButton.findAll('[data-slot="trailingIcon"]')).toHaveLength(1)
    host.unmount()
  })

  it('marks only the default view in the list of views', async () => {
    const { host, viewButton } = await mountSwitcher('staging')

    await viewButton.trigger('click')
    await settle()
    const items = [...document.body.querySelectorAll('[role="menu"] [role="menuitem"]')].map((item) =>
      item.textContent?.trim(),
    )
    expect(items).toEqual(['defaultprod', 'staging', 'Manage views'])
    host.unmount()
  })
})

describe('default view in the sidebar', () => {
  const sidebar = useSidebar()

  afterEach(() => {
    sidebar.open.value = false
    sidebar.collapsed.value = false
  })

  // happy-dom is 1024 px wide, so the docked sidebar is an icon rail without labels: the slideover shows them.
  async function mountSidebar(view: string) {
    const mounted = await mountScreen(AppSidebar, {}, { scope: scope(view) })
    usePreferencesStore().sidebarPinned = false
    sidebar.open.value = true
    await settle()
    const label = [...document.body.querySelectorAll('[data-slot="label"]')].find((element) =>
      element.textContent?.startsWith('View · '),
    )
    return { ...mounted, label }
  }

  it('shows prod right after the label of the default view', async () => {
    const { host, label } = await mountSidebar('default')

    expect(label?.querySelector('[data-slot="linkLabel"]')?.textContent).toBe('View · default')
    expect(label?.querySelector('[data-slot="linkTrailingBadge"]')?.textContent).toBe('prod')
    expect(label?.querySelector('[data-slot="linkTrailing"]')?.className).toContain('ms-0')
    host.unmount()
  })

  it('shows no badge after the label of another view', async () => {
    const { host, label } = await mountSidebar('staging')

    expect(label?.querySelector('[data-slot="linkLabel"]')?.textContent).toBe('View · staging')
    expect(label?.querySelector('[data-slot="linkTrailingBadge"]')).toBeNull()
    host.unmount()
  })
})
