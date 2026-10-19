import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
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
    // a section of the view for the switcher (424-68), which page memory leaves out
    expect(viewLocation(`${base}/key-values`)).toMatchObject({ section: 'key-values', root: true })
    expect(viewLocation(`${base}/key-values/db.host/versions`)).toMatchObject({ section: 'key-values', root: false })
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

  // Values store is a section of the view for the switcher, but it is not remembered (424-68).
  it('leaves Values store out of the memory', async () => {
    await router.push(`${base}/key-values/db.host?q=db`)
    await router.push('/acme/billing/settings')
    await router.push(`${base}/key-values`)
    expect(current()).toBe(`${base}/key-values`)

    // the switcher from Values store to another view, where a key and Annotations were left open
    await router.push('/acme/billing/other/key-values/db.host')
    await router.push('/acme/billing/other/annotations?q=b')
    await router.push(`${base}/key-values`)
    await router.push('/acme/billing/other/key-values')
    expect(current()).toBe('/acme/billing/other/key-values')

    // Values store is another section of the view, so Annotations still come back as they were
    await router.push(`${base}/annotations?q=a`)
    await router.push(`${base}/key-values`)
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

  it('does not redirect links with a query, unremembered pages or the first navigation', async () => {
    await router.push(`${base}/annotations/sbt.northwind`)
    await router.push('/acme/billing/settings')
    await router.push(`${base}/annotations?q=key:sbt.contoso`)
    expect(current()).toBe(`${base}/annotations?q=key:sbt.contoso`)

    // the values store is not remembered
    await router.push(`${base}/key-values/db.password`)
    await router.push('/acme/billing/settings')
    await router.push(`${base}/key-values`)
    expect(current()).toBe(`${base}/key-values`)

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

// Modeling comes back to its last tab, detail and query (424-67). Its plain URL redirects to the first tab before any
// guard runs, so these tests run the generated routes, with that redirect and the redirects of the old URLs.
describe('the page memory of Modeling', () => {
  const modeling = `${base}/modeling`
  let router: Router
  // Registered before the page memory guard: a pass that the guard redirects runs no guard after it.
  const passes = vi.fn()
  const landings = vi.fn()

  beforeEach(async () => {
    router = createRouter({ history: createMemoryHistory(), routes })
    router.beforeEach(() => void passes())
    installPageMemoryGuard(router)
    router.afterEach(() => landings())
    await router.push('/orgs')
    await router.isReady()
  })

  const current = () => router.currentRoute.value.fullPath

  /** Leaves `path` for Annotations, so the next plain Modeling URL comes from another section. */
  async function leave(path: string) {
    await router.push(path)
    await router.push(`${base}/annotations`)
    passes.mockClear()
    landings.mockClear()
  }

  it.each([
    `${modeling}/deleted?cascaded=1`,
    `${modeling}/templates/exe`,
    `${modeling}/compare?key=sbt.northwind&to=staging`,
    `${modeling}/schemas/annotation/sbt.northwind/type/exe`,
  ])('returns to %s in one navigation, and Back returns to the page before', async (path) => {
    await leave(path)

    await router.push(modeling)
    expect(current()).toBe(path)
    // the redirect and its second pass, and one landing
    expect(passes).toHaveBeenCalledTimes(2)
    expect(landings).toHaveBeenCalledTimes(1)

    router.back()
    await vi.waitFor(() => expect(current()).toBe(`${base}/annotations`))
  })

  it('returns from the plain URL with a trailing slash too', async () => {
    await leave(`${modeling}/deleted?cascaded=1`)

    await router.push(`${modeling}/`)
    expect(current()).toBe(`${modeling}/deleted?cascaded=1`)
  })

  it('returns to a location in the spelling of the browser without a loop', async () => {
    // A reload or Back records the address as the browser writes it; the router writes the query of the redirect again.
    await leave(`${modeling}/compare?key=sbt.O%27Brien&to=staging`)

    await router.push(modeling)
    expect(current()).toBe(`${modeling}/compare?key=sbt.O'Brien&to=staging`)
    expect(passes).toHaveBeenCalledTimes(2)
    expect(landings).toHaveBeenCalledTimes(1)
  })

  it('keeps a tab, a query, a hash, an old URL and the first navigation as they are', async () => {
    await leave(`${modeling}/deleted?cascaded=1`)
    await router.push(`${modeling}/templates`)
    expect(current()).toBe(`${modeling}/templates`)

    await leave(`${modeling}/deleted?cascaded=1`)
    await router.push(`${modeling}/schemas`)
    expect(current()).toBe(`${modeling}/schemas`)

    await leave(`${modeling}/deleted?cascaded=1`)
    await router.push(`${modeling}?x=1`)
    expect(current()).toBe(`${modeling}/schemas?x=1`)

    await leave(`${modeling}/deleted?cascaded=1`)
    await router.push(`${modeling}#x`)
    expect(current()).toBe(`${modeling}/schemas#x`)

    // the old pages of a view open their own tab
    await leave(`${modeling}/templates/exe`)
    await router.push(`${base}/schemas`)
    expect(current()).toBe(`${modeling}/schemas`)
    await leave(`${modeling}/templates/exe`)
    await router.push(`${base}/deleted?cascaded=1`)
    expect(current()).toBe(`${modeling}/deleted?cascaded=1`)

    const fresh = createRouter({ history: createMemoryHistory(), routes })
    installPageMemoryGuard(fresh)
    await fresh.push(modeling)
    expect(fresh.currentRoute.value.fullPath).toBe(`${modeling}/schemas`)
  })

  it('opens Schemas from within Modeling and without a remembered location', async () => {
    await router.push(`${base}/annotations`)
    await router.push(modeling)
    expect(current()).toBe(`${modeling}/schemas`)

    // Selecting the open section again starts over.
    await router.push(`${modeling}/deleted?cascaded=1`)
    await router.push(modeling)
    expect(current()).toBe(`${modeling}/schemas`)
  })

  it('keeps the Modeling of each view apart', async () => {
    const staging = '/acme/billing/staging/modeling'
    await router.push(`${modeling}/deleted?cascaded=1`)
    await router.push(`${staging}/templates/exe`)

    // from the Modeling of the other view
    await router.push(modeling)
    expect(current()).toBe(`${modeling}/deleted?cascaded=1`)
    await router.push(staging)
    expect(current()).toBe(`${staging}/templates/exe`)
  })

  it('remembers a query changed in place, nothing of a cancelled navigation, and nothing after clear', async () => {
    await router.push(`${modeling}/deleted`)
    await router.replace(`${modeling}/deleted?cascaded=1`)
    await router.push(`${base}/annotations`)
    const cancel = router.beforeEach((to) => !to.path.endsWith('/templates'))
    await router.push(`${modeling}/templates`)
    cancel()
    expect(current()).toBe(`${base}/annotations`)

    await router.push(modeling)
    expect(current()).toBe(`${modeling}/deleted?cascaded=1`)

    usePageMemoryStore().clear()
    await router.push(`${base}/annotations`)
    await router.push(modeling)
    expect(current()).toBe(`${modeling}/schemas`)
  })

  it('still brings Annotations back from the browser spelling and behind the redirect of the project', async () => {
    await router.push(`${base}/annotations/sbt.northwind?q=type%3Asbt`)
    await router.push('/acme/billing/settings')
    passes.mockClear()

    await router.push(`${base}/annotations`)
    expect(current()).toBe(`${base}/annotations/sbt.northwind?q=type:sbt`)
    expect(passes).toHaveBeenCalledTimes(2)

    // /{org}/{project} opens the annotations of the default view
    await router.push('/acme/billing/settings')
    await router.push('/acme/billing')
    expect(current()).toBe(`${base}/annotations/sbt.northwind?q=type:sbt`)
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
