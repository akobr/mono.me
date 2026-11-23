import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, type PropType } from 'vue'
import type { VueWrapper } from '@vue/test-utils'
import type { Router } from 'vue-router'
import type { QueryClient } from '@tanstack/vue-query'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import AnnotationDetail from '@/components/annotations/AnnotationDetail.vue'
import ViewCopyDialog from '@/components/views/ViewCopyDialog.vue'
import { useViewTransfer } from '@/composables/useViewTransfer'
import { mockHandlers } from '@/mock/all-handlers'
import ConfigurationDetailPage from '@/pages/[org]/[project]/[view]/configurations/[key].vue'
import type { Scope } from '@/stores/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { compareLocation } from '@/utils/view-compare'
import { mountScreen, settle } from '../../helpers'

// "Compare with view" and "Copy to view" in the More actions menus of the annotation and the configuration detail
// (spec "Copy and Compare Annotations and Configurations Between Views", section 8) against the mock API:
// acme/billing has the views default and staging, acme/shipping (Contributor) and northwind/portal (Reader) only
// their default view.

// happy-dom grants no clipboard permission, so VueUse would copy through a hidden textarea; the stand-in records what
// a menu copies
const copy = vi.fn(async (_text: string) => {})
vi.mock('@vueuse/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@vueuse/core')>()),
  useClipboard: () => ({ copy }),
}))

const server = setupServer(...mockHandlers)
// unmounted before the body is cleared, so a closing menu or dialog does not patch removed nodes
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const billing: Scope = { org: 'acme', project: 'billing', view: 'default' }
const shipping: Scope = { org: 'acme', project: 'shipping', view: 'default' }
const portal: Scope = { org: 'northwind', project: 'portal', view: 'default' }
const accessMap = { 'acme.billing': 'Owner', 'acme.shipping': 'Contributor', 'northwind.portal': 'Reader' }

/** northwind/portal, where the user is a Reader, gets a second view. */
function portalWithStaging() {
  server.use(
    http.get('*/api/v1/northwind/portal/views', () =>
      HttpResponse.json([
        { Name: 'default', IsDefault: true, IsRegistered: true },
        { Name: 'staging', IsDefault: false, IsRegistered: true },
      ]),
    ),
  )
}

/** What a Reader's "Compare with view" is: an enabled link to the comparison of the one key, and no Copy to view. */
async function expectReaderCompare(view: { router: Router; host: VueWrapper }, href: string) {
  const compare = item('Compare with view')
  expect(compare.tagName).toBe('A')
  expect(compare.getAttribute('href')).toBe(href)
  expect(compare.hasAttribute('aria-disabled')).toBe(false)
  expect(labels()).not.toContain('Copy to view')
  // a comparison only reads (424-272); the copy writes, and there is no dialog to open
  expect(copyDialog(view).exists()).toBe(false)

  compare.click()
  await settle()
  expect(view.router.currentRoute.value.fullPath).toBe(href)
}

/** The views of the project arrived: only then a menu knows whether there is another view. */
const viewsKnown = (view: { queryClient: QueryClient }, scope: Scope) =>
  vi.waitFor(() =>
    expect(view.queryClient.getQueryState(['v1', scope.org, scope.project, 'views'])?.status).toBe('success'),
  )

const menuItems = () => [...document.body.querySelectorAll<HTMLElement>('[role="menu"] [role="menuitem"]')]
const labels = () => menuItems().map((entry) => entry.querySelector('[data-slot="itemLabel"]')?.textContent?.trim())
const item = (label: string) =>
  menuItems().find((candidate) => candidate.querySelector('[data-slot="itemLabel"]')?.textContent?.trim() === label)!
/** The groups of the open menu, each as the labels of its items. */
const groups = () =>
  [...document.body.querySelectorAll('[role="menu"] [role="group"]')].map((group) =>
    [...group.querySelectorAll('[role="menuitem"] [data-slot="itemLabel"]')].map((label) => label.textContent?.trim()),
  )

async function openMenu() {
  document.body.querySelector<HTMLElement>('button[aria-label="More actions"]')!.click()
  await vi.waitFor(() => expect(menuItems().length).toBeGreaterThan(0))
}

// the copy dialog is teleported to the body
const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
const copyDialog = (view: { host: VueWrapper }) => view.host.findComponent(ViewCopyDialog)

/**
 * What the blocked "Compare with view" and "Copy to view" show and do: disabled, the reason below the label of each,
 * no address and no dialog.
 */
async function expectBlocked(view: { router: Router; host: VueWrapper }, reason: string) {
  for (const label of ['Compare with view', 'Copy to view']) {
    const entry = item(label)
    expect(entry.getAttribute('aria-disabled')).toBe('true')
    expect(entry.hasAttribute('href')).toBe(false)
    expect(entry.querySelector('[data-slot="itemDescription"]')?.textContent?.trim()).toBe(reason)
  }

  const before = view.router.currentRoute.value.fullPath
  item('Compare with view').click()
  await settle()
  expect(view.router.currentRoute.value.fullPath).toBe(before)

  item('Copy to view').click()
  await settle()
  expect(copyDialog(view).props('open')).toBe(false)
  expect(dialog()).toBeNull()
}

/** What "Copy to view" of an open menu shows and does: no link and no reason, and its dialog for the one key. */
async function expectCopy(view: { router: Router; host: VueWrapper }, kind: string, key: string) {
  const copyItem = item('Copy to view')
  // not a link: the dialog opens over the page, and takes what is stored
  expect(copyItem.hasAttribute('href')).toBe(false)
  expect(copyItem.hasAttribute('aria-disabled')).toBe(false)
  expect(copyItem.querySelector('[data-slot="itemDescription"]')).toBeNull()
  expect(copyDialog(view).props('open')).toBe(false)
  expect(dialog()).toBeNull()

  const before = view.router.currentRoute.value.fullPath
  copyItem.click()
  await vi.waitFor(() => expect(dialog()?.textContent).toContain(`Copy ${key} to another view`))

  expect(copyDialog(view).props()).toMatchObject({ open: true, kind, keys: [key], compare: true })
  expect(copyDialog(view).props('target')).toBeUndefined()
  expect(copyDialog(view).props('expected')).toBeUndefined()
  expect(view.router.currentRoute.value.fullPath).toBe(before)
}

describe('More actions of the annotation detail', () => {
  const key = 'sbt.northwind'

  async function mountDetail(scope: Scope, props: Record<string, unknown> = {}) {
    const view = await mountScreen(
      AnnotationDetail,
      { annotationKey: key, ...props },
      { path: `/${scope.org}/${scope.project}/${scope.view}/annotations/${key}`, scope, accessMap },
    )
    mounted.push(view)
    await viewsKnown(view, scope)
    await settle()
    return view
  }

  it('offers Compare with view and Copy to view to a Contributor and above, after the two copies', async () => {
    const view = await mountDetail(billing)
    await openMenu()

    expect(groups()).toEqual([
      ['Show in graph'],
      ['Copy link', 'Copy key'],
      ['Compare with view', 'Copy to view'],
      ['Delete'],
    ])

    // a link, so it opens in a new tab too, and a form with unsaved changes asks before it is left
    const compare = item('Compare with view')
    expect(compare.tagName).toBe('A')
    expect(compare.getAttribute('href')).toBe(
      `/acme/billing/default/modeling/compare/selection?kind=annotations&key=${key}`,
    )
    expect(compare.hasAttribute('aria-disabled')).toBe(false)
    expect(compare.querySelector('[data-slot="itemDescription"]')).toBeNull()

    compare.click()
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe(
      `/acme/billing/default/modeling/compare/selection?kind=annotations&key=${key}`,
    )
  })

  it('opens the copy of the annotation with Copy to view', async () => {
    const view = await mountDetail(billing)
    await openMenu()

    await expectCopy(view, 'annotations', key)
  })

  it('offers them on Graph too', async () => {
    const view = await mountDetail(billing, { section: 'graph' })
    await openMenu()

    expect(groups()).toEqual([
      ['Show in list'],
      ['Copy link', 'Copy key'],
      ['Compare with view', 'Copy to view'],
      ['Delete'],
    ])
    await expectCopy(view, 'annotations', key)
  })

  it('offers them, enabled, to a Contributor of a project with another view', async () => {
    // the user is a Contributor of acme/shipping, which gets a second view here
    server.use(
      http.get('*/api/v1/acme/shipping/views', () =>
        HttpResponse.json([
          { Name: 'default', IsDefault: true, IsRegistered: true },
          { Name: 'staging', IsDefault: false, IsRegistered: true },
        ]),
      ),
    )
    const view = await mountDetail(shipping)
    await openMenu()

    expect(groups()).toEqual([
      ['Show in graph'],
      ['Copy link', 'Copy key'],
      ['Compare with view', 'Copy to view'],
      ['Delete'],
    ])
    expect(item('Compare with view').getAttribute('href')).toBe(
      `/acme/shipping/default/modeling/compare/selection?kind=annotations&key=${key}`,
    )
    await expectCopy(view, 'annotations', key)
  })

  it('offers a Reader Compare with view, without Copy to view and Delete', async () => {
    portalWithStaging()
    const view = await mountDetail(portal)
    await openMenu()

    expect(groups()).toEqual([['Show in graph'], ['Copy link', 'Copy key'], ['Compare with view']])
    await expectReaderCompare(view, `/northwind/portal/default/modeling/compare/selection?kind=annotations&key=${key}`)
  })

  it('keeps the Compare with view of a Reader, disabled with the reason, in a project without another view', async () => {
    const view = await mountDetail(portal)
    await openMenu()

    expect(groups()).toEqual([['Show in graph'], ['Copy link', 'Copy key'], ['Compare with view']])
    const compare = item('Compare with view')
    expect(compare.getAttribute('aria-disabled')).toBe('true')
    expect(compare.hasAttribute('href')).toBe(false)
    expect(compare.querySelector('[data-slot="itemDescription"]')?.textContent?.trim()).toBe(
      'The project has no other view',
    )
    expect(copyDialog(view).exists()).toBe(false)
  })

  it('does not offer them for a deleted annotation', async () => {
    const view = await mountDetail(billing, { deleted: true })
    await openMenu()

    expect(labels()).toEqual(['Show in graph', 'Copy link', 'Copy key'])
    expect(copyDialog(view).exists()).toBe(false)
  })

  it('keeps the items, disabled with the reason, in a project without another view', async () => {
    const view = await mountDetail(shipping)
    await openMenu()

    expect(labels()).toEqual(['Show in graph', 'Copy link', 'Copy key', 'Compare with view', 'Copy to view', 'Delete'])
    await expectBlocked(view, 'The project has no other view')
  })
})

describe('More actions of the configuration detail', () => {
  const key = 'sbt.contoso'
  // the detail with its tab, as the generated routes nest them
  const routes = [
    { path: '/:org/:project/:view/configurations/:key/:tab?', component: { render: () => null } },
    { path: '/:pathMatch(.*)*', component: { render: () => null } },
  ]

  async function mountDetail(scope: Scope) {
    const view = await mountScreen(
      ConfigurationDetailPage,
      {},
      {
        path: `/${scope.org}/${scope.project}/${scope.view}/configurations/${key}/effective`,
        scope,
        accessMap,
        routes,
      },
    )
    mounted.push(view)
    await viewsKnown(view, scope)
    await settle()
    return view
  }

  it('sits before the buttons of the panel', async () => {
    await mountDetail(billing)

    const buttons = [...document.body.querySelectorAll('#configuration-detail button[aria-label]')].map((button) =>
      button.getAttribute('aria-label'),
    )
    expect(buttons.slice(buttons.indexOf('More actions'))).toEqual([
      'More actions',
      'Pin the detail',
      'Open in full screen',
      'Close the detail',
    ])
  })

  it('offers the copies to everyone, and Compare with view and Copy to view to a Contributor and above', async () => {
    const view = await mountDetail(billing)
    await openMenu()

    expect(groups()).toEqual([
      ['Copy link', 'Copy key'],
      ['Compare with view', 'Copy to view'],
    ])

    const compare = item('Compare with view')
    expect(compare.tagName).toBe('A')
    expect(compare.getAttribute('href')).toBe(
      `/acme/billing/default/modeling/compare/selection?kind=configurations&key=${key}`,
    )

    compare.click()
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe(
      `/acme/billing/default/modeling/compare/selection?kind=configurations&key=${key}`,
    )
  })

  it('opens the copy of the configuration with Copy to view', async () => {
    const view = await mountDetail(billing)
    await openMenu()

    await expectCopy(view, 'configurations', key)
    // only a copy of configurations asks about their annotations
    expect(dialog()!.textContent).toContain('Copy missing annotations too')
  })

  it('offers a Reader the copies and Compare with view, without Copy to view', async () => {
    portalWithStaging()
    const view = await mountDetail(portal)
    await openMenu()

    expect(groups()).toEqual([['Copy link', 'Copy key'], ['Compare with view']])
    await expectReaderCompare(
      view,
      `/northwind/portal/default/modeling/compare/selection?kind=configurations&key=${key}`,
    )
  })

  it('keeps the items, disabled with the reason, in a project without another view', async () => {
    const view = await mountDetail(shipping)
    await openMenu()

    expect(labels()).toEqual(['Copy link', 'Copy key', 'Compare with view', 'Copy to view'])
    await expectBlocked(view, 'The project has no other view')
  })

  it('copies the address of the configuration, whatever its tab, and the key', async () => {
    // a Reader has both copies
    await mountDetail(portal)

    await openMenu()
    item('Copy link').click()
    await vi.waitFor(() => expect(document.body.textContent).toContain('Link copied'))
    expect(copy).toHaveBeenLastCalledWith(`${location.origin}/northwind/portal/default/configurations/${key}`)

    await openMenu()
    item('Copy key').click()
    await vi.waitFor(() => expect(copy).toHaveBeenLastCalledWith(key))
  })
})

describe('useViewTransfer', () => {
  let transfer!: ReturnType<typeof useViewTransfer>

  const Probe = defineComponent({
    props: { keys: { type: Array as PropType<string[]>, required: true } },
    setup(props) {
      transfer = useViewTransfer('configurations', () => props.keys)
      return () => null
    },
  })

  const keysOf = (count: number) =>
    Array.from({ length: count }, (_, index) => `exe.northwind.invoicing.job-${String(index).padStart(3, '0')}`)

  async function mountProbe(keys: string[], scope = billing) {
    const view = await mountScreen(Probe, { keys }, { scope, accessMap })
    mounted.push(view)
    return view
  }

  it('gives the address of the comparison while the keys fit in one, and opens the copy on the page', async () => {
    const view = await mountProbe(['usg.northwind.invoicing', 'sbt.contoso'])
    await viewsKnown(view, billing)

    const address = compareLocation(billing, {
      kind: 'configurations',
      keys: ['sbt.contoso', 'usg.northwind.invoicing'],
    })
    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.compareTo.value).toEqual(address)
    // the comparison is a link, the copy an action: its dialog opens where the actions are shown
    expect(transfer.items.value).toEqual([
      { label: 'Compare with view', icon: 'i-lucide-git-compare', to: address },
      { label: 'Copy to view', icon: 'i-lucide-copy-plus', onSelect: expect.any(Function) },
    ])

    expect(transfer.copying.value).toBe(false)
    transfer.items.value[1]!.onSelect!(new Event('select'))
    expect(transfer.copying.value).toBe(true)
    // the copy leaves the page where it is, and keeps no list of keys
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

    // the place that shows the actions closes its dialog through the same state
    transfer.copying.value = false
    expect(transfer.items.value).toHaveLength(2)
  })

  it('gives no address for a long list, whose item keeps the keys and opens the comparison', async () => {
    const keys = keysOf(60)
    const view = await mountProbe(keys)
    await viewsKnown(view, billing)

    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.compareTo.value).toBeUndefined()
    const [compare, copyItem] = transfer.items.value
    expect(transfer.items.value).toHaveLength(2)
    expect(compare).toMatchObject({ label: 'Compare with view', icon: 'i-lucide-git-compare' })
    expect(compare!.to).toBeUndefined()
    expect(compare!.disabled).toBeUndefined()
    // the copy needs no address: it is the same item for a long list
    expect(copyItem).toEqual({ label: 'Copy to view', icon: 'i-lucide-copy-plus', onSelect: expect.any(Function) })
    // nothing is kept until the comparison is opened
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

    copyItem!.onSelect!(new Event('select'))
    await settle()
    expect(transfer.copying.value).toBe(true)
    // the copy keeps no list either
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')

    compare!.onSelect!(new Event('select'))
    await settle()

    const { path, query } = view.router.currentRoute.value
    expect(path).toBe('/acme/billing/default/modeling/compare/selection')
    expect(query.kind).toBe('configurations')
    expect(query.key).toBeUndefined()
    expect(useViewTransferStore().keysOf(String(query.set))).toEqual(keys)
  })

  it('is blocked above 100 keys', async () => {
    const view = await mountProbe(keysOf(101))
    await viewsKnown(view, billing)

    const reason = 'Select up to 100 to compare or copy them at once'
    expect(transfer.blocked.value).toBe(reason)
    expect(transfer.compareTo.value).toBeUndefined()
    // both items stay, disabled, with the reason; neither has an address or an action
    expect(transfer.items.value).toEqual([
      { label: 'Compare with view', icon: 'i-lucide-git-compare', disabled: true, description: reason },
      { label: 'Copy to view', icon: 'i-lucide-copy-plus', disabled: true, description: reason },
    ])
    expect(transfer.copying.value).toBe(false)

    await transfer.openCompare()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

    await view.setProps({ keys: keysOf(100) })
    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.items.value.map((entry) => entry.disabled)).toEqual([undefined, undefined])
  })

  it('is blocked without another view only once the views are known', async () => {
    let answer!: () => void
    const answered = new Promise<void>((resolve) => (answer = resolve))
    server.use(
      http.get('*/api/v1/:org/:project/views', async () => {
        await answered
        return HttpResponse.json([{ Name: 'default', IsDefault: true, IsRegistered: true }])
      }),
    )
    const view = await mountProbe(['sbt.contoso'], shipping)
    await settle()

    // while the views load, the comparison is on: its page says when there is no other view
    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.compareTo.value).toBeDefined()
    expect(transfer.items.value.map((entry) => entry.disabled)).toEqual([undefined, undefined])

    answer()
    await viewsKnown(view, shipping)
    const reason = 'The project has no other view'
    expect(transfer.blocked.value).toBe(reason)
    expect(transfer.compareTo.value).toBeUndefined()
    expect(transfer.items.value).toEqual([
      { label: 'Compare with view', icon: 'i-lucide-git-compare', disabled: true, description: reason },
      { label: 'Copy to view', icon: 'i-lucide-copy-plus', disabled: true, description: reason },
    ])

    await transfer.openCompare()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/shipping/default')
  })

  it('stays on when the views cannot be read', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/views', () => HttpResponse.json({ Message: 'No views' }, { status: 500 })),
    )
    const view = await mountProbe(['sbt.contoso'], shipping)
    await vi.waitFor(() =>
      expect(view.queryClient.getQueryState(['v1', 'acme', 'shipping', 'views'])?.status).toBe('error'),
    )

    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.compareTo.value).toBeDefined()
  })
})
