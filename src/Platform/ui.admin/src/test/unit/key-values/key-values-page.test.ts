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
  async function mountValuesStore(accessMap = { 'acme.billing': 'Owner', 'acme.shipping': 'Contributor' }) {
    // the pages through the generated routes, as in the app, so a switch keeps the page's component
    const view = await mountScreen(RouterView, {}, { path: '/acme/billing/default/key-values', accessMap, routes })
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

  /** The "Include secret values" item of the open Export menu. */
  const includeSecrets = () =>
    [...document.body.querySelectorAll('[role="menuitemcheckbox"]')].find((item) =>
      item.textContent?.includes('Include secret values'),
    )

  async function toggleExportMenu() {
    document.body.querySelector<HTMLElement>('button[aria-label="Export"]')!.click()
    await settle()
  }

  it('asks again for the secret values of an export in another project or view', async () => {
    const view = await mountValuesStore({ 'acme.billing': 'Owner', 'acme.shipping': 'Owner' })

    for (const path of ['/acme/shipping/default/key-values', '/acme/shipping/staging/key-values']) {
      await toggleExportMenu()
      ;(includeSecrets() as HTMLElement).click()
      await settle()
      expect(includeSecrets()?.getAttribute('aria-checked')).toBe('true')
      await toggleExportMenu()

      await view.router.push(path)
      await settle()

      await toggleExportMenu()
      expect(includeSecrets()?.getAttribute('aria-checked')).toBe('false')
      await toggleExportMenu()
    }
  })
})

// The links of the Secrets policy open the All state, as the counts of a level in use include the deleted keys (424-159).
describe('Values store in the All state', () => {
  async function mountAt(path: string, expectedKey: string) {
    const view = await mountScreen(RouterView, {}, { path, accessMap: { 'acme.billing': 'Owner' }, routes })
    mounted.push(view)
    await vi.waitFor(() => expect(document.body.querySelector(`[aria-label="Select ${expectedKey}"]`)).toBeTruthy())
    return view
  }

  const row = (key: string) => document.body.querySelector(`[aria-label="Select ${key}"]`)?.closest('tr')
  const button = (label: string) => [...document.body.querySelectorAll('button')].find((item) => item.textContent?.trim() === label)
  const bulkBar = () => document.body.querySelector('[role="toolbar"][aria-label="Bulk actions"]')

  it('lists the active and the deleted keys and marks the deleted ones', async () => {
    await mountAt('/acme/billing/default/key-values?state=All', 'legacy.endpoint')

    expect(row('db.host')).toBeTruthy()
    expect(row('legacy.endpoint')?.textContent).toContain('Deleted')
    expect(row('db.host')?.textContent).not.toContain('Deleted')
    expect(row('legacy.endpoint')?.textContent).toContain('expires in')
    // the pressed state is the solid variant; Active and Deleted stay outlined
    expect(button('All')?.className).not.toBe(button('Active')?.className)
    expect(button('Active')?.className).toBe(button('Deleted')?.className)
  })

  it('opens the keys of a level from the link of the Secrets policy', async () => {
    await mountAt('/acme/billing/default/key-values?level=Standard&state=All', 'db.password')

    expect(document.body.textContent).not.toContain('No deleted keys')
    expect(row('db.host')).toBeFalsy()
  })

  it('offers no bulk action for a selection of active and deleted keys', async () => {
    await mountAt('/acme/billing/default/key-values?state=All', 'legacy.endpoint')

    document.body.querySelector<HTMLElement>('[aria-label="Select db.host"]')!.click()
    await settle()
    expect(bulkBar()?.textContent).toContain('Delete')

    document.body.querySelector<HTMLElement>('[aria-label="Select legacy.endpoint"]')!.click()
    await settle()
    expect(bulkBar()?.textContent).toContain('Select only active or only deleted keys')
    expect(bulkBar()?.textContent).not.toContain('Restore')
    expect(bulkBar()?.textContent).not.toContain('Change protection')

    document.body.querySelector<HTMLElement>('[aria-label="Select db.host"]')!.click()
    await settle()
    expect(bulkBar()?.textContent).toContain('Restore')
  })

  it('keeps the Deleted state to the deleted keys', async () => {
    await mountAt('/acme/billing/default/key-values?state=Deleted', 'legacy.endpoint')

    expect(row('db.host')).toBeFalsy()
  })
})
