import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { setupServer } from 'msw/node'
import ConfigurationBulkBar from '@/components/configuration/ConfigurationBulkBar.vue'
import ViewCopyDialog from '@/components/views/ViewCopyDialog.vue'
import { mockHandlers } from '@/mock/all-handlers'
import ConfigurationsPage from '@/pages/[org]/[project]/[view]/configurations.vue'
import { useCatalogStore } from '@/stores/catalog'
import { usePageMemoryStore } from '@/stores/page-memory'
import { useViewTransferStore } from '@/stores/view-transfer'
import { canonicalJson, readConfiguration } from '@/utils/view-documents'
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
// unmounted before the body is cleared, so a closing tooltip or dialog does not patch removed nodes
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
const transfers = () => [button('Compare with view'), button('Copy to view')]
/** What the bar shows, in its order: the number of selected rows, the buttons, and the reason beside them. */
const parts = () => [...(toolbar()?.children ?? [])].map((child) => child.textContent?.trim())

// the copy dialog is teleported to the body
const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
const dialogButton = (label: string) =>
  [...(dialog()?.querySelectorAll('button') ?? [])].find((candidate) => candidate.textContent?.trim() === label)
/** A copy is several requests: a slow machine takes longer than the second `vi.waitFor` waits by default. */
const eventually = (assertion: () => void) => vi.waitFor(assertion, { timeout: 3000 })

/** Both buttons are off for one reason, which is one text beside them and the description of both. */
function expectBlocked(reason: string) {
  const [compare, copy] = transfers()
  expect([compare!.disabled, copy!.disabled]).toEqual([true, true])
  const described = compare!.getAttribute('aria-describedby')
  expect(described).toBeTruthy()
  expect(copy!.getAttribute('aria-describedby')).toBe(described)
  const text = document.getElementById(described!)
  expect(text?.textContent).toBe(reason)
  expect(toolbar()!.contains(text)).toBe(true)
  // said once, not once per button
  expect(toolbar()!.textContent!.split(reason)).toHaveLength(2)
}

describe('ConfigurationBulkBar', () => {
  const comparison = '/acme/billing/default/modeling/compare/selection'
  const staging = { org: 'acme', project: 'billing', view: 'staging' }
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
    return { ...view, clear, copyDialog: () => view.host.findComponent(ViewCopyDialog) }
  }

  it('shows the number of selected rows, Compare with view, Copy to view and Clear selection', async () => {
    await mountBar(['sbt.contoso', 'usg.northwind.invoicing'])

    expect(toolbar()).not.toBeNull()
    expect(toolbar()!.querySelector('span')?.textContent).toBe('2 selected')
    // the two actions follow the number of selected rows
    expect(parts()).toEqual(['2 selected', 'Compare with view', 'Copy to view', 'Clear selection'])
    expect([...toolbar()!.querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())).toEqual([
      'Compare with view',
      'Copy to view',
      'Clear selection',
    ])
    // both are on, and nothing explains them
    for (const transfer of transfers()) {
      expect(transfer.disabled).toBe(false)
      expect(transfer.hasAttribute('aria-describedby')).toBe(false)
    }
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

  it('opens the copy of the selected configurations with Copy to view', async () => {
    const view = await mountBar(['usg.northwind.invoicing', 'sbt.contoso'])
    expect(view.copyDialog().props('open')).toBe(false)
    expect(dialog()).toBeNull()

    button('Copy to view').click()
    await settle()

    // the dialog of configurations, for the selected keys; the target is picked in it
    expect(view.copyDialog().props()).toMatchObject({
      open: true,
      kind: 'configurations',
      keys: ['usg.northwind.invoicing', 'sbt.contoso'],
      compare: true,
    })
    expect(view.copyDialog().props('target')).toBeUndefined()
    expect(view.copyDialog().props('expected')).toBeUndefined()
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Copy 2 configurations to another view'))
    // only a copy of configurations asks about their annotations
    expect(dialog()!.textContent).toContain('Copy missing annotations too')
    // the page stays, with its selection
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
    expect(view.clear).not.toHaveBeenCalled()
  })

  it('copies the selected configurations to the other view and keeps the selection', async () => {
    // the own configuration of sbt.contoso differs between the views
    const key = 'sbt.contoso'
    const source = (await readConfiguration({ ...staging, view: 'default' }, key)).own
    expect(canonicalJson((await readConfiguration(staging, key)).own)).not.toBe(canonicalJson(source))
    const view = await mountBar([key])

    button('Copy to view').click()
    // staging is the only other view, so it is the target
    await eventually(() => expect(dialogButton('Copy')?.disabled).toBe(false))
    dialogButton('Copy')!.click()
    await eventually(() => expect(dialog()?.textContent).toContain('Copied to staging'))

    expect(canonicalJson((await readConfiguration(staging, key)).own)).toBe(canonicalJson(source))
    // from a list, the copy can be compared next
    expect(dialogButton('Compare with staging')).toBeDefined()

    dialogButton('Done')!.click()
    await eventually(() => expect(view.copyDialog().props('open')).toBe(false))
    // the rows of this view did not change: they stay selected
    expect(view.clear).not.toHaveBeenCalled()
    expect(parts()[0]).toBe('1 selected')
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
  })

  it('clears the selection with Clear selection', async () => {
    const view = await mountBar(['sbt.contoso'])

    button('Clear selection').click()
    await settle()

    expect(view.clear).toHaveBeenCalledOnce()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
  })

  it('disables both in a project without another view, with the reason once beside them', async () => {
    // acme/shipping has only its default view, and the user is a Contributor there
    const view = await mountBar(['sbt.contoso'], 'shipping')

    expectBlocked('The project has no other view')
    // the reason follows the two buttons
    expect(parts()).toEqual([
      '1 selected',
      'Compare with view',
      'Copy to view',
      'The project has no other view',
      'Clear selection',
    ])

    for (const transfer of transfers()) {
      transfer.click()
    }
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/shipping/default')
    expect(view.copyDialog().props('open')).toBe(false)
    expect(dialog()).toBeNull()
    // the selection can still be cleared
    expect(button('Clear selection').disabled).toBe(false)
  })

  it.each(['Compare with view', 'Copy to view'])(
    'shows the reason as the tooltip of the disabled %s',
    async (label) => {
      await mountBar(['sbt.contoso'], 'shipping')

      button(label).dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse' }))

      await vi.waitFor(() =>
        expect(document.body.querySelector('[role="tooltip"]')?.textContent?.trim()).toBe(
          'The project has no other view',
        ),
      )
    },
  )

  it('disables both above 100 selected configurations, and formats their number', async () => {
    const view = await mountBar(keysOf(1234))

    expect(toolbar()!.querySelector('span')?.textContent).toBe(`${format(1234)} selected`)
    expectBlocked('Select up to 100 to compare or copy them at once')

    button('Copy to view').click()
    await settle()
    expect(view.copyDialog().props('open')).toBe(false)
  })

  it('enables both with exactly 100 selected configurations', async () => {
    await mountBar(keysOf(100))

    expect(transfers().map((transfer) => transfer.disabled)).toEqual([false, false])
    expect(toolbar()!.textContent).not.toContain('Select up to 100')
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

  it('copies the selected configurations from the page, and keeps them selected', async () => {
    const view = await mountPage()
    memory().selection = { 'usg.northwind.invoicing': true, 'sbt.contoso': true }
    await vi.waitFor(() =>
      expect(view.queryClient.getQueryState(['v1', 'acme', 'billing', 'views'])?.status).toBe('success'),
    )
    await settle()

    button('Copy to view').click()
    await settle()

    // the dialog of the bar gets the selected keys that are in the catalog
    const copyDialog = view.host.findComponent(ViewCopyDialog)
    expect(copyDialog.props()).toMatchObject({ open: true, kind: 'configurations' })
    expect([...(copyDialog.props('keys') as string[])].toSorted()).toEqual(['sbt.contoso', 'usg.northwind.invoicing'])

    await eventually(() => expect(dialogButton('Copy')?.disabled).toBe(false))
    dialogButton('Copy')!.click()
    // usg.northwind.invoicing is the same in both views, sbt.contoso is not
    await eventually(() => expect(dialog()?.textContent).toContain('1 updated, 1 unchanged'))
    dialogButton('Done')!.click()
    await eventually(() => expect(copyDialog.props('open')).toBe(false))

    expect(view.router.currentRoute.value.path).toBe('/acme/billing/default/configurations')
    expect(memory().selection).toEqual({ 'usg.northwind.invoicing': true, 'sbt.contoso': true })
    expect(selected()).toBe('2 selected')
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
