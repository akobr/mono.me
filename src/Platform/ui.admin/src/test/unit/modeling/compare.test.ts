import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import type { View } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import ComparePage from '@/pages/[org]/[project]/[view]/modeling/compare.vue'
import ViewsPage from '@/pages/[org]/[project]/views.vue'
import { contextGuard } from '@/router/guards/context'
import { useContextStore } from '@/stores/context'
import { mountScreen, settle } from '../../helpers'

// Compare views never compares a view with itself (424-64): Compare on the Views page is off on the row of the default
// view, the only row of a project without another view, and Compare views takes no ?to= naming the current view.

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
  server.events.removeAllListeners()
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const key = 'exe.northwind.invoicing.prod'
const views = (...names: string[]) =>
  names.map((name): View => ({ Name: name, IsDefault: name === 'default', IsRegistered: name !== 'legacy' }))

/** The paths of the requests the page makes from now on. */
function recordRequests() {
  const paths: string[] = []
  server.events.on('request:start', ({ request }) => void paths.push(new URL(request.url).pathname))
  return paths
}

describe('Compare on the Views page', () => {
  async function mountViews(project: string, accessMap?: Record<string, string>) {
    const view = await mountScreen(
      ViewsPage,
      {},
      { path: `/acme/${project}/views`, scope: { org: 'acme', project, view: 'default' }, accessMap },
    )
    mounted.push(view)
    // a project page has no view (the context guard sets it from the route)
    useContextStore().set({ org: 'acme', project })
    await settle()
    return view
  }

  const row = (name: string) =>
    [...document.body.querySelectorAll('li')].find((item) => item.querySelector('a')?.textContent?.trim() === name)
  const compare = (name: string) =>
    [...(row(name)?.querySelectorAll<HTMLElement>('a, button') ?? [])].find(
      (element) => element.textContent?.trim() === 'Compare',
    )

  async function hint(element: HTMLElement) {
    element.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse' }))
    await vi.waitFor(() => expect(document.body.querySelector('[role="tooltip"]')).not.toBeNull())
    return document.body.querySelector('[role="tooltip"]')!.textContent?.trim()
  }

  it('is disabled in a project without another view, and a click goes nowhere', async () => {
    // acme/shipping has only its default view, and the user is a Contributor there
    const view = await mountViews('shipping', { 'acme.shipping': 'Contributor' })

    const button = compare('default')!
    expect(button.getAttribute('aria-disabled')).toBe('true')
    expect(button.hasAttribute('href')).toBe(false)
    expect(button.tabIndex).toBe(-1)

    button.click()
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/shipping/views')
    expect(await hint(button)).toBe('The project has no other view to compare with')
  })

  it('compares the default view with another view, never with itself', async () => {
    // acme/billing has default and staging
    const view = await mountViews('billing')

    const staging = compare('staging')!
    expect(staging.getAttribute('href')).toBe('/acme/billing/default/modeling/compare?to=staging')
    expect(staging.hasAttribute('aria-disabled')).toBe(false)

    const own = compare('default')!
    expect(own.getAttribute('aria-disabled')).toBe('true')
    expect(own.hasAttribute('href')).toBe(false)
    expect(await hint(own)).toBe('The other views are compared with this one')

    staging.click()
    await settle()
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default/modeling/compare?to=staging')
  })

  it('compares the default view with a discovered view', async () => {
    server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json(views('default', 'legacy'))))
    await mountViews('billing')

    expect(compare('legacy')?.getAttribute('href')).toBe('/acme/billing/default/modeling/compare?to=legacy')
    expect(compare('default')?.getAttribute('aria-disabled')).toBe('true')
  })

  it('is not offered to a Reader', async () => {
    const view = await mountViews('billing', { 'acme.billing': 'Reader' })

    expect(view.queryClient.getQueryState(['v1', 'acme', 'billing', 'views', { discover: false }])?.status).toBe(
      'success',
    )
    expect(row('staging')).toBeDefined()
    expect(compare('staging')).toBeUndefined()
    expect(compare('default')).toBeUndefined()
  })
})

describe('Compare views', () => {
  async function mountCompare(project: string, query: string) {
    const view = await mountScreen(
      ComparePage,
      {},
      {
        path: `/acme/${project}/default/modeling/compare?${query}`,
        scope: { org: 'acme', project, view: 'default' },
        accessMap: { 'acme.billing': 'Owner', 'acme.shipping': 'Owner' },
        routes,
      },
    )
    mounted.push(view)
    // the store follows the route as in the app, before a navigation lands
    view.router.beforeEach(contextGuard)
    await settle()
    return view
  }

  const text = () => document.body.textContent ?? ''
  const diffs = (paths: string[]) => paths.filter((path) => path.includes('/diff/'))

  it('takes the only other view instead of ?to= naming the current view', async () => {
    const requests = recordRequests()
    const view = await mountCompare('billing', `to=default&key=${key}`)

    await vi.waitFor(() => expect(view.router.currentRoute.value.query.to).toBe('staging'))
    expect(view.router.currentRoute.value.query.key).toBe(key)
    await vi.waitFor(() =>
      expect(diffs(requests)).toEqual([`/api/v1/acme/billing/default/configuration/${key}/diff/staging`]),
    )
  })

  it('requests nothing for ?to= naming the current view in a project without another view', async () => {
    const requests = recordRequests()
    const view = await mountCompare('shipping', `to=default&key=${key}`)

    await vi.waitFor(() => expect(text()).toContain('No other view to compare with'))
    expect(view.queryClient.getQueryState(['v1', 'acme', 'shipping', 'views'])?.status).toBe('success')
    expect(text()).not.toContain('Compare a configuration across views')
    expect(diffs(requests)).toEqual([])
    expect(requests.filter((path) => path.includes(`/configuration/${key}`))).toEqual([])
  })

  it('keeps ?to= naming a discovered view, which the list of views does not have', async () => {
    const requests = recordRequests()
    const view = await mountCompare('shipping', `to=legacy&key=${key}`)

    await vi.waitFor(() =>
      expect(diffs(requests)).toEqual([`/api/v1/acme/shipping/default/configuration/${key}/diff/legacy`]),
    )
    expect(view.router.currentRoute.value.query.to).toBe('legacy')
    expect(text()).not.toContain('No other view to compare with')
  })

  it('lets the view switcher open the view that ?to= names', async () => {
    // the Views page opened default with ?to=staging; the switcher keeps the tab and drops the query
    const view = await mountCompare('billing', `to=staging&key=${key}`)
    await vi.waitFor(() => expect(text()).toContain('---'))
    const requests = recordRequests()

    await expect(view.router.push(`/acme/billing/staging/modeling/compare?key=${key}`)).resolves.toBeUndefined()
    expect(view.router.currentRoute.value.path).toBe('/acme/billing/staging/modeling/compare')
    // there, the only other view is default; staging was never compared with itself on the way
    await vi.waitFor(() => expect(view.router.currentRoute.value.query.to).toBe('default'))
    await vi.waitFor(() =>
      expect(diffs(requests)).toEqual([`/api/v1/acme/billing/staging/configuration/${key}/diff/default`]),
    )
  })

  it('lets the switcher open another project whose views are known', async () => {
    const requests = recordRequests()
    const view = await mountCompare('shipping', `key=${key}`)
    await vi.waitFor(() => expect(text()).toContain('No other view to compare with'))
    view.queryClient.setQueryData(['v1', 'acme', 'billing', 'views'], views('default', 'staging'))

    await expect(view.router.push(`/acme/billing/default/modeling/compare?key=${key}`)).resolves.toBeUndefined()
    expect(view.router.currentRoute.value.path).toBe('/acme/billing/default/modeling/compare')
    await vi.waitFor(() => expect(view.router.currentRoute.value.query.to).toBe('staging'))
    await vi.waitFor(() =>
      expect(diffs(requests)).toEqual([`/api/v1/acme/billing/default/configuration/${key}/diff/staging`]),
    )
  })

  it('asks for another view while the views load', async () => {
    let answer!: () => void
    const answered = new Promise<void>((resolve) => (answer = resolve))
    server.use(
      http.get('*/api/v1/:org/:project/views', async () => {
        await answered
        return HttpResponse.json(views('default'))
      }),
    )
    await mountCompare('shipping', '')

    expect(text()).toContain('Compare a configuration across views')
    expect(text()).not.toContain('No other view to compare with')

    // once the views are known, none of them is another view
    answer()
    await vi.waitFor(() => expect(text()).toContain('No other view to compare with'))
    expect(text()).not.toContain('Compare a configuration across views')
  })

  it('asks for another view when the views fail', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/views', () => HttpResponse.json({ Message: 'No views' }, { status: 500 })),
    )
    await mountCompare('shipping', '')

    expect(text()).toContain('Compare a configuration across views')
    expect(text()).not.toContain('No other view to compare with')
  })
})
