import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { setupServer } from 'msw/node'
import ConfigurationBulkBar from '@/components/configuration/ConfigurationBulkBar.vue'
import { mockHandlers } from '@/mock/all-handlers'
import ConfigurationsPage from '@/pages/[org]/[project]/[view]/configurations.vue'
import { useCatalogStore } from '@/stores/catalog'
import { usePageMemoryStore } from '@/stores/page-memory'
import { useViewTransferStore } from '@/stores/view-transfer'
import { mountScreen, settle } from '../../helpers'

// The multi-selection of Configurations (spec "Copy and Compare Annotations and Configurations Between Views", section
// 8) against the mock API: acme/billing has the views default and staging, acme/shipping (Contributor) and
// northwind/portal (Reader) only their default view. The table is virtualized and renders no rows without a layout:
// its header, with "Select all rows", is rendered anyway, and the test of a row gives the table a height.

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
// unmounted before the body is cleared, so a closing tooltip does not patch removed nodes
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const accessMap = { 'acme.billing': 'Owner', 'acme.shipping': 'Contributor', 'northwind.portal': 'Reader' }
const format = (value: number) => new Intl.NumberFormat().format(value)

const toolbar = () => document.body.querySelector<HTMLElement>('[role="toolbar"][aria-label="Bulk actions"]')
const button = (label: string) =>
  [...(toolbar()?.querySelectorAll('button') ?? [])].find((candidate) => candidate.textContent?.trim() === label)!

describe('ConfigurationBulkBar', () => {
  const comparison = '/acme/billing/default/modeling/compare/selection'
  const keysOf = (count: number) =>
    Array.from({ length: count }, (_, index) => `exe.northwind.invoicing.job-${String(index).padStart(4, '0')}`)

  async function mountBar(keys: string[], project = 'billing') {
    const clear = vi.fn()
    const view = await mountScreen(
      ConfigurationBulkBar,
      { keys, onClear: clear },
      { scope: { org: 'acme', project, view: 'default' }, accessMap },
    )
    mounted.push(view)
    // the bar knows whether there is another view once the views of the project arrived
    await vi.waitFor(() =>
      expect(view.queryClient.getQueryState(['v1', 'acme', project, 'views'])?.status).toBe('success'),
    )
    await settle()
    return { ...view, clear }
  }

  it('shows the number of selected rows, Compare with view and Clear selection', async () => {
    await mountBar(['sbt.contoso', 'usg.northwind.invoicing'])

    expect(toolbar()).not.toBeNull()
    expect(toolbar()!.querySelector('span')?.textContent).toBe('2 selected')
    expect([...toolbar()!.querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())).toEqual([
      'Compare with view',
      'Clear selection',
    ])
    expect(button('Compare with view').disabled).toBe(false)
    expect(button('Compare with view').hasAttribute('aria-describedby')).toBe(false)
  })

  it('opens the comparison of the selected configurations and keeps the selection', async () => {
    const view = await mountBar(['usg.northwind.invoicing', 'sbt.contoso'])

    button('Compare with view').click()
    await settle()

    // the keys of the address are sorted, the own configurations are compared, and no target is picked yet
    expect(view.router.currentRoute.value.fullPath).toBe(
      `${comparison}?kind=configurations&key=sbt.contoso&key=usg.northwind.invoicing`,
    )
    expect(view.clear).not.toHaveBeenCalled()
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()
  })

  it('keeps a selection that is too long for an address, and opens the comparison by its id', async () => {
    const keys = keysOf(100)
    const view = await mountBar(keys)
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

    button('Compare with view').click()
    await settle()

    const { path, query } = view.router.currentRoute.value
    expect(path).toBe(comparison)
    expect(query.kind).toBe('configurations')
    expect(query.key).toBeUndefined()
    expect(useViewTransferStore().keysOf(String(query.set))).toEqual(keys)
  })

  it('clears the selection with Clear selection', async () => {
    const view = await mountBar(['sbt.contoso'])

    button('Clear selection').click()
    await settle()

    expect(view.clear).toHaveBeenCalledOnce()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
  })

  it('is disabled in a project without another view, with the reason beside it and as its tooltip', async () => {
    // acme/shipping has only its default view, and the user is a Contributor there
    const view = await mountBar(['sbt.contoso'], 'shipping')

    const compare = button('Compare with view')
    expect(compare.disabled).toBe(true)
    const reason = document.getElementById(compare.getAttribute('aria-describedby')!)
    expect(reason?.textContent).toBe('The project has no other view')
    expect(toolbar()!.contains(reason)).toBe(true)
    compare.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse' }))
    await vi.waitFor(() =>
      expect(document.body.querySelector('[role="tooltip"]')?.textContent?.trim()).toBe(
        'The project has no other view',
      ),
    )

    compare.click()
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/shipping/default')
    // the selection can still be cleared
    expect(button('Clear selection').disabled).toBe(false)
  })

  it('is disabled above 100 selected configurations, and formats their number', async () => {
    await mountBar(keysOf(1234))

    expect(toolbar()!.querySelector('span')?.textContent).toBe(`${format(1234)} selected`)
    const compare = button('Compare with view')
    expect(compare.disabled).toBe(true)
    expect(document.getElementById(compare.getAttribute('aria-describedby')!)?.textContent).toBe(
      'Select up to 100 to compare or copy them at once',
    )
  })
})

describe('the selection of Configurations in the page memory', () => {
  const billing = { org: 'acme', project: 'billing', view: 'default' }

  it('starts empty, is kept per view, and is gone after sign-out', () => {
    const store = usePageMemoryStore()

    expect(store.page(billing, 'configurations')).toEqual({ selection: {}, scrollTop: 0 })

    store.page(billing, 'configurations').selection = { 'sbt.contoso': true }
    expect(store.page(billing, 'configurations').selection).toEqual({ 'sbt.contoso': true })
    expect(store.page({ ...billing, view: 'staging' }, 'configurations').selection).toEqual({})
    // Annotations keeps its own selection
    expect(store.page(billing, 'annotations').selection).toEqual({})

    // sign-out clears the store
    store.clear()
    expect(store.page(billing, 'configurations').selection).toEqual({})
  })

  async function mountPage(scope = billing) {
    await useCatalogStore().forgetSnapshot(scope)
    const view = await mountScreen(
      ConfigurationsPage,
      {},
      { path: `/${scope.org}/${scope.project}/${scope.view}/configurations`, scope, accessMap },
    )
    mounted.push(view)
    await vi.waitFor(() => expect(useCatalogStore().configurations?.size).toBeGreaterThan(0))
    await vi.waitFor(() => expect(selectAll()).not.toBeNull())
    return view
  }

  const memory = (scope = billing) => usePageMemoryStore().page(scope, 'configurations')
  const configured = () => [...useCatalogStore().configurations!.keys()]
  const selectAll = () => document.body.querySelector<HTMLElement>('[aria-label="Select all rows"]')
  const selected = () => toolbar()?.querySelector('span')?.textContent

  async function toggleAll() {
    selectAll()!.click()
    await settle()
  }

  it('shows the bar for the remembered selection, without the keys that are gone', async () => {
    memory().selection = { 'sbt.gone': true }
    await mountPage()
    expect(toolbar()).toBeNull()

    const [first, second] = configured()
    memory().selection = { [first!]: true, 'sbt.gone': true, [second!]: true }
    await settle()

    expect(selected()).toBe('2 selected')
    expect(selectAll()!.getAttribute('aria-checked')).toBe('mixed')
  })

  it('selects every row with the header checkbox, and clears with Clear selection', async () => {
    await mountPage()
    expect(selectAll()!.getAttribute('aria-checked')).toBe('false')

    await toggleAll()

    const keys = configured()
    expect(Object.keys(memory().selection).toSorted()).toEqual(keys.toSorted())
    expect(selected()).toBe(`${format(keys.length)} selected`)
    expect(selectAll()!.getAttribute('aria-checked')).toBe('true')

    button('Clear selection').click()
    await settle()

    expect(memory().selection).toEqual({})
    expect(toolbar()).toBeNull()
    expect(selectAll()!.getAttribute('aria-checked')).toBe('false')
  })

  it('selects a row with its checkbox, which does not open the detail as the rest of the row does', async () => {
    // happy-dom has no layout; given a height, the virtualized table renders its first rows
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(600)
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(1000)
    const view = await mountPage()
    const list = '/acme/billing/default/configurations'
    const key = configured().toSorted((a, b) => a.localeCompare(b))[0]!
    const checkbox = await vi.waitFor(() => {
      const element = document.body.querySelector<HTMLElement>(`[aria-label="Select ${key}"]`)
      expect(element).not.toBeNull()
      return element!
    })
    const row = checkbox.closest('tr')!
    // the checkbox is the first cell, the type the second
    expect(row.cells[0]!.contains(checkbox)).toBe(true)
    expect(row.cells).toHaveLength(7)

    checkbox.click()
    await settle()

    expect(memory().selection).toEqual({ [key]: true })
    expect(selected()).toBe('1 selected')
    expect(checkbox.getAttribute('aria-checked')).toBe('true')
    expect(selectAll()!.getAttribute('aria-checked')).toBe('mixed')
    expect(view.router.currentRoute.value.path).toBe(list)

    checkbox.click()
    await settle()
    expect(memory().selection).toEqual({})
    expect(toolbar()).toBeNull()
    expect(view.router.currentRoute.value.path).toBe(list)

    // the name of the row opens its detail
    row.cells[2]!.click()
    await settle()
    expect(view.router.currentRoute.value.path).toBe(`${list}/${key}`)
  })

  it('keeps the keys that the filter hides, and the header checkbox counts only the shown rows', async () => {
    const view = await mountPage()
    // a filter on a type with several configurations: the configurations of the other types are hidden
    const codes = [...new Set(configured().map((key) => key.slice(0, 3)))]
    const code = codes.find((type) => configured().filter((key) => key.startsWith(`${type}.`)).length > 1)!
    const shown = configured().filter((key) => key.startsWith(`${code}.`))
    const hidden = configured().find((key) => !key.startsWith(`${code}.`))!
    expect(hidden).toBeDefined()
    await view.router.replace({ query: { q: `type:${code}` } })
    await vi.waitFor(() =>
      expect(document.body.textContent).toContain(`${format(shown.length)} of ${format(configured().length)}`),
    )

    memory().selection = { [hidden]: true }
    await settle()
    // one row is selected, but none of the shown ones
    expect(selected()).toBe('1 selected')
    expect(selectAll()!.getAttribute('aria-checked')).toBe('false')

    memory().selection = { [hidden]: true, [shown[0]!]: true }
    await settle()
    expect(selectAll()!.getAttribute('aria-checked')).toBe('mixed')

    // from some to all of the shown rows
    await toggleAll()
    expect(Object.keys(memory().selection).toSorted()).toEqual([hidden, ...shown].toSorted())
    expect(selected()).toBe(`${format(shown.length + 1)} selected`)
    expect(selectAll()!.getAttribute('aria-checked')).toBe('true')

    // and back to none of them: the hidden key stays selected
    await toggleAll()
    expect(memory().selection).toEqual({ [hidden]: true })
    expect(selected()).toBe('1 selected')
    expect(selectAll()!.getAttribute('aria-checked')).toBe('false')
  })

  it('opens the comparison of the selected configurations from the page', async () => {
    const view = await mountPage()
    const [first, second] = configured().toSorted()
    memory().selection = { [second!]: true, [first!]: true }
    await vi.waitFor(() =>
      expect(view.queryClient.getQueryState(['v1', 'acme', 'billing', 'views'])?.status).toBe('success'),
    )
    await settle()

    button('Compare with view').click()
    await settle()

    const { path, query } = view.router.currentRoute.value
    expect(path).toBe('/acme/billing/default/modeling/compare/selection')
    expect(query).toEqual({ kind: 'configurations', key: [first, second] })
    // the selection is still there when the page comes back
    expect(Object.keys(memory().selection)).toHaveLength(2)
  })

  it('shows the checkboxes but no bar to a Reader', async () => {
    const portal = { org: 'northwind', project: 'portal', view: 'default' }
    await mountPage(portal)

    await toggleAll()

    expect(Object.keys(memory(portal).selection).length).toBeGreaterThan(0)
    expect(selectAll()!.getAttribute('aria-checked')).toBe('true')
    expect(toolbar()).toBeNull()
  })
})
