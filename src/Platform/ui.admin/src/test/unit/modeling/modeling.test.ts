import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, type SetupContext } from 'vue'
import { createMemoryHistory, createRouter, RouterView, useRoute } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import type { View } from '@42for.net/2splatform.sdk'
import ModelingHeader from '@/components/modeling/ModelingHeader.vue'
import { mockHandlers } from '@/mock/all-handlers'
import ModelingPage from '@/pages/[org]/[project]/[view]/modeling.vue'
import { hasOtherView, modelingPath, modelingTab, movedToModeling } from '@/utils/modeling'
import { mountScreen, settle } from '../../helpers'

// The Modeling page of a view (424-38): Schemas, Templates, Export & Import, Deleted and Compare views as tabs, each a
// page of its own below modeling/, locked below Contributor.

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
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }
const views = (...names: string[]) => names.map((name): View => ({ Name: name, IsDefault: name === 'default' }))

describe('modeling paths', () => {
  it('builds the path of the page and of each tab', () => {
    expect(modelingPath(scope)).toBe('/acme/billing/default/modeling')
    expect(modelingPath(scope, 'schemas')).toBe('/acme/billing/default/modeling/schemas')
    expect(modelingPath({ ...scope, view: 'staging' }, 'export-import')).toBe(
      '/acme/billing/staging/modeling/export-import',
    )
  })

  it('reads the tab of a path, also below a detail and with a query or a hash', () => {
    expect(modelingTab('/acme/billing/default/modeling/templates')).toBe('templates')
    expect(modelingTab('/acme/billing/default/modeling/schemas/annotation/sbt.a/type/exe')).toBe('schemas')
    expect(modelingTab('/acme/billing/default/modeling/deleted?cascaded=1')).toBe('deleted')
    expect(modelingTab('/acme/billing/default/modeling/compare#x')).toBe('compare')
    expect(modelingTab('/acme/billing/modeling/modeling/export-import')).toBe('export-import')
  })

  it('reads no tab outside the tabs of Modeling', () => {
    expect(modelingTab('/acme/billing/default/modeling')).toBeUndefined()
    expect(modelingTab('/acme/billing/default/modeling/foo')).toBeUndefined()
    // the old URL, and the Schemas page of a view named modeling
    expect(modelingTab('/acme/billing/default/schemas')).toBeUndefined()
    expect(modelingTab('/acme/billing/modeling/schemas')).toBeUndefined()
    expect(modelingTab('/acme/billing/monitor')).toBeUndefined()
  })

  it('moves an old path below modeling/ and keeps the rest of it as it is', () => {
    expect(movedToModeling('/acme/billing/default/schemas')).toBe('/acme/billing/default/modeling/schemas')
    expect(movedToModeling('/acme/billing/default/schemas/annotation/sbt.a/type/exe')).toBe(
      '/acme/billing/default/modeling/schemas/annotation/sbt.a/type/exe',
    )
    expect(movedToModeling('/acme/billing/default/deleted')).toBe('/acme/billing/default/modeling/deleted')
    // the path of the route is encoded, and it stays so
    expect(movedToModeling('/acme/billing/default/schemas/annotation/sbt.caf%C3%A9%2Fx')).toBe(
      '/acme/billing/default/modeling/schemas/annotation/sbt.caf%C3%A9%2Fx',
    )
    expect(movedToModeling('/acme/billing/default/templates/')).toBe('/acme/billing/default/modeling/templates/')
    expect(movedToModeling('/acme/billing/modeling/compare')).toBe('/acme/billing/modeling/modeling/compare')
  })

  it('knows whether the project has a view other than the current one', () => {
    expect(hasOtherView(views('default', 'staging'), 'default')).toBe(true)
    expect(hasOtherView(views('default', 'staging'), 'staging')).toBe(true)
    expect(hasOtherView(views('default'), 'default')).toBe(false)
    expect(hasOtherView([], 'default')).toBe(false)
    expect(hasOtherView(undefined, 'default')).toBe(false)
    // a view without a name is no view to compare with
    expect(hasOtherView([{ Name: 'default' }, { IsDefault: false }], 'default')).toBe(false)
  })
})

describe('ModelingHeader', () => {
  const tabs = () => [...document.body.querySelectorAll<HTMLButtonElement>('button[role="tab"]')]
  const tab = (label: string) => tabs().find((candidate) => candidate.textContent?.trim() === label)!
  const selected = () => tabs().find((candidate) => candidate.getAttribute('aria-selected') === 'true')

  async function mountHeader(path: string, project = 'billing') {
    const view = await mountScreen(ModelingHeader, {}, { path, scope: { ...scope, project } })
    await settle()
    return view
  }

  it('shows the five tabs in order under the navbar of Modeling', async () => {
    await mountHeader('/acme/billing/default/modeling/schemas')

    expect(document.body.querySelector('[data-navbar]')?.getAttribute('data-navbar')).toBe('Modeling')
    expect(tabs().map((candidate) => candidate.textContent?.trim())).toEqual([
      'Schemas',
      'Templates',
      'Export & Import',
      'Deleted',
      'Compare views',
    ])
  })

  it.each([
    ['/acme/billing/default/modeling/schemas', 'Schemas'],
    ['/acme/billing/default/modeling/schemas/annotation/sbt.a/type/exe', 'Schemas'],
    ['/acme/billing/default/modeling/templates/exe', 'Templates'],
    ['/acme/billing/default/modeling/export-import', 'Export & Import'],
    ['/acme/billing/default/modeling/deleted', 'Deleted'],
    ['/acme/billing/default/modeling/compare', 'Compare views'],
  ])('selects the tab of %s', async (path, label) => {
    await mountHeader(path)

    expect(selected()?.textContent?.trim()).toBe(label)
  })

  it('opens another tab as a new page, without the query of the previous one', async () => {
    const view = await mountHeader('/acme/billing/default/modeling/deleted?cascaded=1')
    const push = vi.spyOn(view.router, 'push')

    tab('Templates').dispatchEvent(new MouseEvent('mousedown', { button: 0, bubbles: true }))
    await settle()

    expect(push).toHaveBeenCalledWith('/acme/billing/default/modeling/templates')
    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default/modeling/templates')
    expect(selected()?.textContent?.trim()).toBe('Templates')

    // Back returns to the previous tab, with its query
    view.router.back()
    await vi.waitFor(() =>
      expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default/modeling/deleted?cascaded=1'),
    )
    await settle()
    expect(selected()?.textContent?.trim()).toBe('Deleted')
  })

  it('keeps the tab of the path when the navigation is cancelled', async () => {
    const view = await mountHeader('/acme/billing/default/modeling/deleted')
    // what Keep editing does when a dirty editor asks before it is left
    view.router.beforeEach(() => false)

    tab('Schemas').dispatchEvent(new MouseEvent('mousedown', { button: 0, bubbles: true }))
    await settle()

    expect(view.router.currentRoute.value.fullPath).toBe('/acme/billing/default/modeling/deleted')
    expect(selected()?.textContent?.trim()).toBe('Deleted')
  })

  describe('focus', () => {
    // Each tab is a page with its own header, so the header is mounted again for every tab.
    const Page = {
      setup: () => {
        const route = useRoute()
        return () => h(ModelingHeader, { key: route.path })
      },
    }
    const press = (target: HTMLElement, key: string) =>
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))

    it('moves to the tab of the opened page after a change from the tabs', async () => {
      const view = await mountScreen(Page, {}, { path: '/acme/billing/default/modeling/schemas' })
      await settle()
      const before = tab('Templates')

      before.focus()
      press(before, 'Enter')
      await settle()

      expect(view.router.currentRoute.value.path).toBe('/acme/billing/default/modeling/templates')
      expect(before.isConnected).toBe(false)
      expect(document.activeElement).toBe(tab('Templates'))
      expect(selected()).toBe(tab('Templates'))
    })

    it('stays on the tabs of the page when the navigation is cancelled', async () => {
      const view = await mountScreen(Page, {}, { path: '/acme/billing/default/modeling/schemas' })
      await settle()
      // what Keep editing does when a dirty editor asks before it is left
      const keepEditing = view.router.beforeEach(() => false)
      const templates = tab('Templates')

      templates.focus()
      press(templates, 'Enter')
      await settle()

      expect(view.router.currentRoute.value.path).toBe('/acme/billing/default/modeling/schemas')
      expect(templates.isConnected).toBe(true)
      expect(document.activeElement).toBe(templates)

      // a later page, opened from elsewhere, does not take the focus
      keepEditing()
      await view.router.push('/acme/billing/default/modeling/deleted')
      await settle()
      expect(selected()).toBe(tab('Deleted'))
      expect(document.activeElement).not.toBe(tab('Deleted'))
    })

    it('leaves the focus alone when the tab changes from elsewhere', async () => {
      const view = await mountScreen(Page, {}, { path: '/acme/billing/default/modeling/schemas' })
      await settle()

      await view.router.push('/acme/billing/default/modeling/compare')
      await settle()

      expect(selected()).toBe(tab('Compare views'))
      expect(document.activeElement).not.toBe(tab('Compare views'))
    })
  })

  it('shows what the page puts on the right of the navbar', async () => {
    const Page = defineComponent({
      setup: () => () => h(ModelingHeader, null, { right: () => h('button', { type: 'button' }, 'New schema') }),
    })
    await mountScreen(Page, {}, { path: '/acme/billing/default/modeling/schemas' })
    await settle()

    expect(document.body.querySelector('[data-navbar="Modeling"] button')?.textContent).toBe('New schema')
  })

  describe('Compare views', () => {
    const compareDisabled = () => tab('Compare views').disabled

    it('is disabled when the project is known to have no other view', async () => {
      // acme/shipping has only its default view
      const view = await mountHeader('/acme/shipping/default/modeling/schemas', 'shipping')

      expect(view.queryClient.getQueryState(['v1', 'acme', 'shipping', 'views'])?.status).toBe('success')
      expect(compareDisabled()).toBe(true)
      expect(tabs().filter((candidate) => candidate.disabled)).toHaveLength(1)
    })

    it('is enabled when the project has another view', async () => {
      // acme/billing has staging too
      const view = await mountHeader('/acme/billing/default/modeling/schemas')

      expect(view.queryClient.getQueryState(['v1', 'acme', 'billing', 'views'])?.status).toBe('success')
      expect(compareDisabled()).toBe(false)
      expect(tabs().filter((candidate) => candidate.disabled)).toEqual([])
    })

    it('is enabled while the views load', async () => {
      let answer!: () => void
      const answered = new Promise<void>((resolve) => (answer = resolve))
      server.use(
        http.get('*/api/v1/:org/:project/views', async () => {
          await answered
          return HttpResponse.json(views('default'))
        }),
      )
      const view = await mountHeader('/acme/shipping/default/modeling/schemas', 'shipping')

      expect(view.queryClient.getQueryState(['v1', 'acme', 'shipping', 'views'])?.status).toBe('pending')
      expect(compareDisabled()).toBe(false)

      // once the views are known, none of them is another view
      answer()
      await vi.waitFor(() => expect(compareDisabled()).toBe(true))
    })

    it.each([500, 403])('is enabled when the views fail with %i', async (status) => {
      server.use(http.get('*/api/v1/:org/:project/views', () => HttpResponse.json({ Message: 'No views' }, { status })))
      const view = await mountHeader('/acme/shipping/default/modeling/schemas', 'shipping')

      expect(view.queryClient.getQueryState(['v1', 'acme', 'shipping', 'views'])?.status).toBe('error')
      expect(compareDisabled()).toBe(false)
    })
  })
})

describe('the Modeling page', () => {
  it('shows a Reader one lock and no tabs', async () => {
    const view = await mountScreen(
      ModelingPage,
      {},
      { path: '/acme/billing/default/modeling/schemas', accessMap: { 'acme.billing': 'Reader' } },
    )
    await settle()

    expect(document.body.querySelector('[data-navbar]')?.getAttribute('data-navbar')).toBe('Modeling')
    expect(view.host.text()).toContain('Contributors only')
    expect(view.host.text()).toContain("available to the project's Contributors")
    expect(document.body.querySelectorAll('[role="tab"]')).toHaveLength(0)
    // no tab page is mounted, so none of their queries runs
    expect(view.inner().findComponent(RouterView).exists()).toBe(false)
  })

  it('is only the outlet of the tab pages for a Contributor', async () => {
    const view = await mountScreen(
      ModelingPage,
      {},
      { path: '/acme/billing/default/modeling', accessMap: { 'acme.billing': 'Contributor' } },
    )
    view.router.addRoute({
      path: '/acme/billing/default/modeling/schemas',
      component: { render: () => h('section', { 'data-tab': 'schemas' }) },
    })
    await view.router.push('/acme/billing/default/modeling/schemas')
    await settle()

    expect(view.host.text()).not.toContain('Contributors only')
    // the tab page itself, without an element around it, so its panels stay children of the dashboard group
    expect((view.inner().element as HTMLElement).dataset.tab).toBe('schemas')
  })
})

describe('the old URLs', () => {
  it.each([
    [
      '/acme/billing/default/schemas',
      '/acme/billing/default/modeling/schemas',
      '/[org]/[project]/[view]/modeling/schemas',
    ],
    [
      '/acme/billing/default/schemas/annotation/sbt.a/type/exe?tab=versions#x',
      '/acme/billing/default/modeling/schemas/annotation/sbt.a/type/exe?tab=versions#x',
      '/[org]/[project]/[view]/modeling/schemas/annotation/[key]/type/[type]',
    ],
    [
      '/acme/billing/default/templates/exe',
      '/acme/billing/default/modeling/templates/exe',
      '/[org]/[project]/[view]/modeling/templates/[type]',
    ],
    [
      '/acme/billing/default/export-import',
      '/acme/billing/default/modeling/export-import',
      '/[org]/[project]/[view]/modeling/export-import',
    ],
    [
      '/acme/billing/default/deleted?cascaded=1',
      '/acme/billing/default/modeling/deleted?cascaded=1',
      '/[org]/[project]/[view]/modeling/deleted',
    ],
    [
      '/acme/billing/default/compare?to=staging',
      '/acme/billing/default/modeling/compare?to=staging',
      '/[org]/[project]/[view]/modeling/compare',
    ],
  ])('redirect %s to %s, so saved links keep working', async (from, to, name) => {
    const router = createRouter({ history: createMemoryHistory(), routes })

    await router.push(from)

    expect(router.currentRoute.value.fullPath).toBe(to)
    expect(router.currentRoute.value.name).toBe(name)
  })

  it('keep an encoded key as it is', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes })

    await router.push('/acme/billing/default/schemas/annotation/sbt.caf%C3%A9')

    expect(router.currentRoute.value.path).toBe('/acme/billing/default/modeling/schemas/annotation/sbt.caf%C3%A9')
    expect(router.currentRoute.value.params).toMatchObject({ key: 'sbt.café' })
  })
})

describe('the Modeling URL', () => {
  it.each([
    ['/acme/billing/default/modeling', '/acme/billing/default/modeling/schemas'],
    ['/acme/billing/default/modeling?x=1', '/acme/billing/default/modeling/schemas?x=1'],
    ['/acme/billing/default/modeling#x', '/acme/billing/default/modeling/schemas#x'],
    // a view named modeling
    ['/acme/billing/modeling/modeling', '/acme/billing/modeling/modeling/schemas'],
  ])('opens the first tab: %s lands on %s', async (from, to) => {
    const router = createRouter({ history: createMemoryHistory(), routes })

    await router.push(from)

    expect(router.currentRoute.value.fullPath).toBe(to)
    expect(router.currentRoute.value.name).toBe('/[org]/[project]/[view]/modeling/schemas')
  })

  it('shows Page not found for an unknown tab', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes })

    await router.push('/acme/billing/default/modeling/foo')

    expect(router.currentRoute.value.fullPath).toBe('/acme/billing/default/modeling/foo')
    expect(router.currentRoute.value.name).toBe('/[...path]')
  })
})
