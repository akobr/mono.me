/* eslint-disable vue/one-component-per-file -- a page with the list, and a parent that leaves and opens it again */
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { setupServer } from 'msw/node'
import { createAccessPoint, getAccount } from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed } from '@/mock/db'
import { useProjectSummaries } from '@/queries/projects'
import { useSessionStore } from '@/stores/session'
import { mountScreen, settle } from '../../helpers'

// The projects list of the overview and Settings (424-78, spec section 7): fetched again when the projects of the
// session's AccessMap differ from the listed ones, against the mock API of the e2e mode.

const server = setupServer(...mockHandlers)
const mounted: { host: { unmount: () => void } }[] = []
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.events.removeAllListeners()
  server.resetHandlers()
  document.body.innerHTML = ''
})
afterAll(() => server.close())

const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

/** The number of requests of the list from now on. */
function recordLists() {
  const lists: string[] = []
  server.events.on('request:start', ({ request }) => {
    if (new URL(request.url).pathname.endsWith('/access/projects')) {
      lists.push(request.url)
    }
  })
  return lists
}

const Descriptions = defineComponent({
  setup() {
    const projects = useProjectSummaries()
    return () =>
      h(
        'ul',
        ['acme.billing', 'acme.returns'].map((key) =>
          h('li', { 'data-key': key }, projects.summaryOf(key)?.Description ?? ''),
        ),
      )
  },
})

/** The list in a page that can be left and opened again, with the same query cache and the app's 30 s of freshness. */
async function mountPages() {
  const shown = ref(false)
  const Pages = defineComponent({ setup: () => () => (shown.value ? h(Descriptions) : null) })
  const view = await mountScreen(Pages, {}, { accessMap })
  mounted.push(view)
  view.queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000 } })
  shown.value = true
  await settle()
  return { shown }
}

const description = (key: string) => document.body.querySelector(`[data-key="${key}"]`)?.textContent

/** A new project of the user, as "New project" creates it, and the reloaded account. */
async function createProject() {
  await createAccessPoint({ Organization: 'acme', Project: 'returns', ProjectDescription: 'Returns of Acme.' })
  useSessionStore().setAccount(await getAccount())
}

describe('useProjectSummaries', () => {
  it('fetches the list again when the projects of the account change, not when a role does', async () => {
    const lists = recordLists()
    await mountPages()

    expect(lists).toHaveLength(1)
    expect(description('acme.billing')).toBe(createSeed('default').accessPoints['acme.billing']!.Description)

    const session = useSessionStore()
    session.setAccount({ ...session.account!, AccessMap: { ...accessMap, 'acme.shipping': 'Reader' } })
    await settle()
    expect(lists).toHaveLength(1)

    await createProject()
    await vi.waitFor(() => expect(description('acme.returns')).toBe('Returns of Acme.'))
    expect(lists).toHaveLength(2)
  })

  it('fetches a cached list again when a page opens after the projects changed', async () => {
    const lists = recordLists()
    const { shown } = await mountPages()
    shown.value = false
    await settle()

    // The list is fresh for 30 s, but it was fetched for other projects.
    await createProject()
    shown.value = true
    await vi.waitFor(() => expect(description('acme.returns')).toBe('Returns of Acme.'))
    expect(lists).toHaveLength(2)
  })

  it('asks once for a project the list leaves out', async () => {
    const lists = recordLists()
    await mountPages()

    // A key without its access point is not listed.
    const session = useSessionStore()
    session.setAccount({ ...session.account!, AccessMap: { ...accessMap, 'acme.gone': 'Reader' } })
    await settle(8)
    expect(lists).toHaveLength(2)
  })
})
