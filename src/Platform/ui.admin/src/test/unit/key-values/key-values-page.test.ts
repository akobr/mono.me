import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { RouterView } from 'vue-router'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import { mockHandlers } from '@/mock/all-handlers'
import { contextGuard } from '@/router/guards/context'
import { mountScreen, settle } from '../../helpers'

// The switcher keeps Values store when the project or the view changes (424-68). The route record stays the same, so
// the page stays mounted, and its selection must not follow the user into the other scope.

// the navbar needs the dashboard shell; the stand-in shows its title and what the page puts on its right
vi.mock('@/components/shell/AppNavbar.vue', async () => {
  const vue = await import('vue')
  return {
    default: {
      name: 'AppNavbar',
      props: ['title'],
      setup:
        (props: { title?: string }, { slots }: SetupContext) =>
        () =>
          vue.h('header', { 'data-navbar': props.title }, slots.right?.()),
    },
  }
})

const server = setupServer(...mockHandlers)
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

describe('Values store through the switcher', () => {
  async function mountValuesStore() {
    // the pages through the generated routes, as in the app, so a switch keeps the page's component
    const view = await mountScreen(
      RouterView,
      {},
      {
        path: '/acme/billing/default/key-values',
        accessMap: { 'acme.billing': 'Owner', 'acme.shipping': 'Contributor' },
        routes,
      },
    )
    mounted.push(view)
    view.router.beforeEach(contextGuard)
    await vi.waitFor(() => expect(checkbox('db.host')).toBeTruthy())
    return view
  }

  const text = () => document.body.textContent ?? ''
  const checkbox = (key: string) => document.body.querySelector<HTMLElement>(`[aria-label="Select ${key}"]`)
  const bulkBar = () => document.body.querySelector('[role="toolbar"][aria-label="Bulk actions"]')

  async function select(key: string) {
    checkbox(key)!.click()
    await settle()
    expect(bulkBar()?.textContent).toContain('1 selected')
  }

  it('clears the selection when the project changes to a view of the same name', async () => {
    const view = await mountValuesStore()
    await select('db.host')

    await view.router.push('/acme/shipping/default/key-values')
    await settle()

    expect(bulkBar()).toBeNull()
    await vi.waitFor(() => expect(text()).toContain('No key values in this view'))
  })

  it('clears the selection when the view changes', async () => {
    const view = await mountValuesStore()
    await select('db.host')

    await view.router.push('/acme/billing/staging/key-values')
    await settle()

    expect(bulkBar()).toBeNull()
    await vi.waitFor(() => expect(text()).toContain('sql.staging'))
    expect(checkbox('db.host')?.getAttribute('aria-checked')).toBe('false')
  })
})
