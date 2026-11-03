import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { SetupContext } from 'vue'
import { RouterView } from 'vue-router'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import type { MachineAccess } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, saveDb } from '@/mock/db'
import { useContextStore } from '@/stores/context'
import { mountScreen, settle } from '../../helpers'

// A Reader has no access to Machines (424-164, ACC-MCH-08). The API refuses the list below Contributor, but a Debug
// build (DEV_AUTH) skips its role checks of users and answers 200, so the page checks the role before it asks.

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

// Ada owns acme/billing, contributes to acme/shipping and reads northwind/portal.
const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

const listed: MachineAccess = { Id: 'mach-listed-by-debug-api', Scopes: ['AnnotationRead'], CredentialKind: 'ApiKey' }

/** Answers the machine list and the machine with 200 whatever the role, as a Debug API does; counts the requests. */
function asDebugApi() {
  const requests: string[] = []
  server.use(
    http.get('*/api/v1/:org/:project/access/machines', ({ request }) => {
      requests.push(new URL(request.url).pathname)
      return HttpResponse.json([listed])
    }),
    http.get('*/api/v1/:org/:project/access/machines/:id', ({ request }) => {
      requests.push(new URL(request.url).pathname)
      return HttpResponse.json(listed)
    }),
  )
  return requests
}

async function mountMachines(path: string, map: Record<string, string> = accessMap) {
  const [, org, project] = path.split('/')
  const view = await mountScreen(
    RouterView,
    {},
    { path, scope: { org: org!, project: project!, view: 'default' }, accessMap: map, routes },
  )
  mounted.push(view)
  await settle(6)
  return view
}

const text = () => document.body.textContent ?? ''
const headings = () => [...document.body.querySelectorAll('h2')].map((heading) => heading.textContent?.trim())
const button = (name: string, root: ParentNode = document.body) =>
  [...root.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === name)

/** The section under the heading "Machine access". */
const machineAccess = () =>
  [...document.body.querySelectorAll('section')].find(
    (section) => section.querySelector('h2')?.textContent?.trim() === 'Machine access',
  )!

const noAccessIn = (root: ParentNode) =>
  [...root.querySelectorAll('h2')].find((heading) => heading.textContent?.trim() === 'No access')

describe('Machines page by role', () => {
  it('shows No access with Retry to a Reader without asking for the machines, also when the API would list them', async () => {
    const requests = asDebugApi()

    await mountMachines('/northwind/portal/machines')

    const section = machineAccess()
    expect(noAccessIn(section)).toBeTruthy()
    expect(section.textContent).toContain('Machines need the Contributor role or a higher one in this project.')
    expect(button('Retry', section)).toBeTruthy()
    expect(section.querySelector('table')).toBeNull()
    expect(text()).not.toContain(listed.Id)
    expect(button('New machine')).toBeUndefined()
    expect(headings()).not.toContain('Shared certificates')
    expect(headings()).not.toContain('Trusted issuers')
    expect(requests).toEqual([])
  })

  it('reloads the account on Retry, so a role granted since lists the machines', async () => {
    await mountMachines('/northwind/portal/machines')
    expect(noAccessIn(machineAccess())).toBeTruthy()

    const db = loadDb()
    db.account!.AccessMap!['northwind.portal'] = 'Contributor'
    db.machines = { ...db.machines, 'northwind.portal': [listed] }
    saveDb(db)
    button('Retry', machineAccess())!.click()

    await vi.waitFor(() => expect(machineAccess().querySelector('table')?.textContent).toContain(listed.Id))
    expect(noAccessIn(machineAccess())).toBeUndefined()
    expect(button('New machine')).toBeTruthy()
  })

  it('keeps No access on Retry while the role stays Reader', async () => {
    const requests = asDebugApi()
    await mountMachines('/northwind/portal/machines')

    button('Retry', machineAccess())!.click()
    await settle(6)

    expect(noAccessIn(machineAccess())).toBeTruthy()
    expect(requests).toEqual([])
  })

  it('lists the machines of a Contributor, without the sections of Administrators', async () => {
    await mountMachines('/acme/billing/machines', { 'acme.billing': 'Contributor' })

    await vi.waitFor(() => expect(machineAccess().querySelector('table')?.textContent).toContain('mach-invoicing-worker'))
    expect(noAccessIn(machineAccess())).toBeUndefined()
    expect(button('New machine')).toBeTruthy()
    expect(headings()).not.toContain('Shared certificates')
    expect(headings()).not.toContain('Trusted issuers')
  })

  it('does not flash No access to an Owner while the context guard clears the project on the way out', async () => {
    await mountMachines('/acme/billing/machines', { 'acme.billing': 'Owner' })
    await vi.waitFor(() => expect(machineAccess().querySelector('table')).toBeTruthy())

    // the guard of a navigation to the organization, before its page has loaded
    useContextStore().set({ org: 'acme' })
    await settle()

    expect(noAccessIn(machineAccess())).toBeUndefined()
  })

  it('shows No access in the detail of a machine to a Reader without asking for it', async () => {
    const requests = asDebugApi()

    await mountMachines(`/northwind/portal/machines/${listed.Id}`)

    const panel = document.body.querySelector('#machine-detail') ?? document.body
    expect(noAccessIn(panel)).toBeTruthy()
    expect(button('Retry', panel)).toBeTruthy()
    expect(text()).not.toContain('Runtime scopes')
    expect(requests).toEqual([])
  })
})
