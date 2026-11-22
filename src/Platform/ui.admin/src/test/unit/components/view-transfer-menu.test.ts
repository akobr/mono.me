import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, type PropType } from 'vue'
import type { Router } from 'vue-router'
import type { QueryClient } from '@tanstack/vue-query'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import AnnotationDetail from '@/components/annotations/AnnotationDetail.vue'
import { useViewTransfer } from '@/composables/useViewTransfer'
import { mockHandlers } from '@/mock/all-handlers'
import ConfigurationDetailPage from '@/pages/[org]/[project]/[view]/configurations/[key].vue'
import type { Scope } from '@/stores/context'
import { useViewTransferStore } from '@/stores/view-transfer'
import { compareLocation } from '@/utils/view-compare'
import { mountScreen, settle } from '../../helpers'

// "Compare with view" in the More actions menus of the annotation and the configuration detail (spec "Copy and
// Compare Annotations and Configurations Between Views", section 8) against the mock API: acme/billing has the views
// default and staging, acme/shipping (Contributor) and northwind/portal (Reader) only their default view.

// happy-dom grants no clipboard permission, so VueUse would copy through a hidden textarea; the stand-in records what
// a menu copies
const copy = vi.fn(async (_text: string) => {})
vi.mock('@vueuse/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@vueuse/core')>()),
  useClipboard: () => ({ copy }),
}))

const server = setupServer(...mockHandlers)
// unmounted before the body is cleared, so a closing menu does not patch removed nodes
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

/** What the blocked "Compare with view" shows and does: disabled, its reason below its label, no address. */
async function expectBlocked(view: { router: Router }, reason: string) {
  const compare = item('Compare with view')
  expect(compare.getAttribute('aria-disabled')).toBe('true')
  expect(compare.hasAttribute('href')).toBe(false)
  expect(compare.querySelector('[data-slot="itemDescription"]')?.textContent?.trim()).toBe(reason)

  const before = view.router.currentRoute.value.fullPath
  compare.click()
  await settle()
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

  it('offers Compare with view to a Contributor and above, between the copies and Delete', async () => {
    const view = await mountDetail(billing)
    await openMenu()

    expect(groups()).toEqual([['Show in graph'], ['Copy link', 'Copy key'], ['Compare with view'], ['Delete']])

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

  it('offers it on Graph too', async () => {
    await mountDetail(billing, { section: 'graph' })
    await openMenu()

    expect(groups()).toEqual([['Show in list'], ['Copy link', 'Copy key'], ['Compare with view'], ['Delete']])
  })

  it('does not offer it to a Reader', async () => {
    await mountDetail(portal)
    await openMenu()

    expect(groups()).toEqual([['Show in graph'], ['Copy link', 'Copy key']])
  })

  it('does not offer it for a deleted annotation', async () => {
    await mountDetail(billing, { deleted: true })
    await openMenu()

    expect(labels()).toEqual(['Show in graph', 'Copy link', 'Copy key'])
  })

  it('keeps the item, disabled with the reason, in a project without another view', async () => {
    const view = await mountDetail(shipping)
    await openMenu()

    expect(labels()).toEqual(['Show in graph', 'Copy link', 'Copy key', 'Compare with view', 'Delete'])
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

  it('offers the copies to everyone and Compare with view to a Contributor and above', async () => {
    const view = await mountDetail(billing)
    await openMenu()

    expect(groups()).toEqual([['Copy link', 'Copy key'], ['Compare with view']])

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

  it('offers only the copies to a Reader', async () => {
    await mountDetail(portal)
    await openMenu()

    expect(groups()).toEqual([['Copy link', 'Copy key']])
  })

  it('keeps the item, disabled with the reason, in a project without another view', async () => {
    const view = await mountDetail(shipping)
    await openMenu()

    expect(labels()).toEqual(['Copy link', 'Copy key', 'Compare with view'])
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

  it('gives the address of the comparison while the keys fit in one', async () => {
    const view = await mountProbe(['usg.northwind.invoicing', 'sbt.contoso'])
    await viewsKnown(view, billing)

    const address = compareLocation(billing, {
      kind: 'configurations',
      keys: ['sbt.contoso', 'usg.northwind.invoicing'],
    })
    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.compareTo.value).toEqual(address)
    expect(transfer.items.value).toEqual([{ label: 'Compare with view', icon: 'i-lucide-git-compare', to: address }])
  })

  it('gives no address for a long list, whose item keeps the keys and opens the comparison', async () => {
    const keys = keysOf(60)
    const view = await mountProbe(keys)
    await viewsKnown(view, billing)

    expect(transfer.blocked.value).toBeUndefined()
    expect(transfer.compareTo.value).toBeUndefined()
    const [compare] = transfer.items.value
    expect(compare).toMatchObject({ label: 'Compare with view', icon: 'i-lucide-git-compare' })
    expect(compare!.to).toBeUndefined()
    expect(compare!.disabled).toBeUndefined()
    // nothing is kept until the comparison is opened
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

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
    expect(transfer.items.value).toEqual([
      { label: 'Compare with view', icon: 'i-lucide-git-compare', disabled: true, description: reason },
    ])

    await transfer.openCompare()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default')
    expect(sessionStorage.getItem('2s-admin:view-transfer')).toBeNull()

    await view.setProps({ keys: keysOf(100) })
    expect(transfer.blocked.value).toBeUndefined()
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

    answer()
    await viewsKnown(view, shipping)
    expect(transfer.blocked.value).toBe('The project has no other view')
    expect(transfer.compareTo.value).toBeUndefined()

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
