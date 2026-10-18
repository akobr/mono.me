import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { installPageMemoryGuard } from '@/router/guards/page-memory'
import { usePageMemoryStore } from '@/stores/page-memory'
import { viewLocation } from '@/utils/context-paths'

const base = '/acme/billing/default'

describe('view locations', () => {
  it('reads the view, the section and whether the path is the section list', () => {
    expect(viewLocation(`${base}/annotations`)).toEqual({
      scope: { org: 'acme', project: 'billing', view: 'default' },
      section: 'annotations',
      root: true,
    })
    expect(viewLocation(`${base}/annotations/sbt.northwind/configuration?q=x`)).toMatchObject({
      section: 'annotations',
      root: false,
    })
    expect(viewLocation('/acme/billing/settings')).toBeUndefined()
    expect(viewLocation('/orgs')).toBeUndefined()
  })
})

describe('page memory guard', () => {
  let router: Router

  beforeEach(async () => {
    router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
    })
    installPageMemoryGuard(router)
    await router.push('/orgs')
    await router.isReady()
  })

  const current = () => router.currentRoute.value.fullPath

  it('returns to the filter, the detail and the tab the user left', async () => {
    await router.push(`${base}/annotations?q=type:sbt&mode=tree`)
    await router.push(`${base}/annotations/sbt.northwind/configuration?q=type:sbt&mode=tree`)
    await router.push('/acme/billing/settings')

    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations/sbt.northwind/configuration?q=type:sbt&mode=tree`)

    // Back returns to the page the user came from, not to the plain list.
    router.back()
    await vi.waitFor(() => expect(current()).toBe('/acme/billing/settings'))
  })

  it('keeps each section and each view apart', async () => {
    await router.push(`${base}/annotations?q=a`)
    await router.push(`${base}/configurations/sbt.contoso/effective`)
    await router.push(`${base}/graph?layout=radial&focus=sbt.contoso`)
    await router.push('/acme/billing/other/annotations?q=b')

    await router.push(`${base}/configurations`)
    expect(current()).toBe(`${base}/configurations/sbt.contoso/effective`)
    await router.push(`${base}/graph`)
    expect(current()).toBe(`${base}/graph?layout=radial&focus=sbt.contoso`)
    // The view switcher goes to the same section of the other view.
    await router.push('/acme/billing/other/annotations')
    expect(current()).toBe('/acme/billing/other/annotations?q=b')
    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations?q=a`)
  })

  it('leaves the plain list alone when it is opened from within the section', async () => {
    await router.push(`${base}/annotations/sbt.northwind?q=x`)
    // Closing the detail, clearing the filter or selecting the open section again.
    await router.push(`${base}/annotations?q=x`)
    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations`)

    await router.push('/acme/billing/settings')
    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations`)
  })

  it('does not redirect links with a query, unremembered sections or the first navigation', async () => {
    await router.push(`${base}/annotations/sbt.northwind`)
    await router.push(`${base}/modeling/templates/exe`)
    await router.push(`${base}/annotations?q=key:sbt.contoso`)
    expect(current()).toBe(`${base}/annotations?q=key:sbt.contoso`)

    await router.push(`${base}/modeling/templates`)
    await router.push(`${base}/modeling/templates`)
    expect(current()).toBe(`${base}/modeling/templates`)

    const fresh = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
    })
    installPageMemoryGuard(fresh)
    await fresh.push(`${base}/annotations`)
    expect(fresh.currentRoute.value.fullPath).toBe(`${base}/annotations`)
  })

  it('records nothing for a cancelled navigation and forgets everything on clear', async () => {
    await router.push(`${base}/annotations?q=kept`)
    const remove = router.beforeEach((to) => !to.fullPath.includes('cancelled'))
    await router.push(`${base}/annotations?q=cancelled`)
    remove()
    await router.push('/orgs')
    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations?q=kept`)

    usePageMemoryStore().clear()
    await router.push('/orgs')
    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations`)
  })
})

describe('page memory store', () => {
  it('creates one memory per view and section and keeps it', () => {
    const store = usePageMemoryStore()
    const scope = { org: 'acme', project: 'billing', view: 'default' }

    const memory = store.page(scope, 'annotations')
    memory.expanded = new Set(['sbt.northwind'])
    expect(store.page(scope, 'annotations').expanded.has('sbt.northwind')).toBe(true)
    expect(store.page({ ...scope, view: 'other' }, 'annotations').expanded.size).toBe(0)
    expect(store.page(scope, 'graph').hidden.size).toBe(0)
  })
})
