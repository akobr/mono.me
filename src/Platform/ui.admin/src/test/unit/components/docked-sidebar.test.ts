import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useSidebar } from '@/composables/useSidebar'
import { mockHandlers } from '@/mock/all-handlers'
import { mountScreen, settle } from '../../helpers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// Where UDashboardSidebar of DefaultLayout keeps its size and whether it is collapsed.
const storageKey = '2s-admin-sidebar-sidebar'
const expanded = '{"size":20,"collapsed":false}'

// happy-dom is 1024 px wide, so this is the width between lg and xl (424-168).
describe('docked sidebar between lg and xl', () => {
  const sidebar = useSidebar()
  let unmount: (() => void) | undefined

  // The module state of a page that has just loaded at this width.
  beforeEach(() => {
    sidebar.collapsed.value = true
  })

  afterEach(() => {
    unmount?.()
    sidebar.collapsed.value = false
  })

  async function mountLayout() {
    const { host } = await mountScreen(DefaultLayout, {})
    unmount = () => host.unmount()
    await settle()
    const root = () => document.getElementById(storageKey)!
    const button = (name: string) => document.querySelector<HTMLButtonElement>(`button[aria-label="${name}"]`)
    return { root, button }
  }

  it('starts as the icon rail when a wider window stored it expanded', async () => {
    localStorage.setItem(storageKey, expanded)
    const { root, button } = await mountLayout()

    expect(root().dataset.collapsed).toBe('true')
    expect(button('Expand sidebar')).not.toBeNull()
    expect(button('Unpin sidebar')).toBeNull()
    expect(sidebar.collapsed.value).toBe(true)
    // The start stores nothing, so a later start at another width finds what was there.
    expect(localStorage.getItem(storageKey)).toBe(expanded)
  })

  it('takes the stored size when it is expanded', async () => {
    localStorage.setItem(storageKey, expanded)
    const { root, button } = await mountLayout()

    button('Expand sidebar')!.click()
    await settle()

    expect(root().dataset.collapsed).toBe('false')
    expect(root().style.getPropertyValue('--width')).toBe('20%')
    expect(button('Collapse sidebar')).not.toBeNull()
    expect(sidebar.collapsed.value).toBe(false)
  })

  it('stays expanded when the layout comes back after a bare route', async () => {
    localStorage.setItem(storageKey, expanded)
    const first = await mountLayout()
    first.button('Expand sidebar')!.click()
    await settle()
    unmount!()

    const { root, button } = await mountLayout()

    expect(root().dataset.collapsed).toBe('false')
    expect(button('Collapse sidebar')).not.toBeNull()
  })

  it('keeps its state when the layout comes back after another window stored the rail', async () => {
    localStorage.setItem(storageKey, expanded)
    const first = await mountLayout()
    first.button('Expand sidebar')!.click()
    await settle()
    unmount!()
    const rail = '{"size":22,"collapsed":true}'
    localStorage.setItem(storageKey, rail)

    const { root } = await mountLayout()

    expect(root().dataset.collapsed).toBe('false')
    expect(sidebar.collapsed.value).toBe(false)
    expect(localStorage.getItem(storageKey)).toBe(rail)
  })

  it.each([
    ['the rail', true],
    ['the expanded sidebar', false],
  ])('keeps %s when another window stores an expanded sidebar', async (_name, collapsed) => {
    localStorage.setItem(storageKey, expanded)
    const { root, button } = await mountLayout()
    if (!collapsed) {
      button('Expand sidebar')!.click()
      await settle()
    }

    window.dispatchEvent(
      new StorageEvent('storage', {
        key: storageKey,
        newValue: '{"size":22,"collapsed":false}',
        storageArea: localStorage,
      }),
    )
    await settle()

    expect(root().dataset.collapsed).toBe(String(collapsed))
    expect(sidebar.collapsed.value).toBe(collapsed)
  })
})
