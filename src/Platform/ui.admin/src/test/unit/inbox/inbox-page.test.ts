import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { createApp, type SetupContext } from 'vue'
import { RouterView } from 'vue-router'
import { useToast } from '@nuxt/ui/composables'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { routes } from 'vue-router/auto-routes'
import {
  dismissViewInboxItem,
  getViewExport,
  getViewImport,
  startViewExport,
  startViewImport,
  type AccountRole,
} from '@42for.net/2splatform.sdk'
import InboxItemRow from '@/components/inbox/InboxItemRow.vue'
import ImportResult from '@/components/transfer/ImportResult.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, saveDb } from '@/mock/db'
import { inboxOf, pushInboxItem, systemActor, type InboxPush, type StoredInboxItem } from '@/mock/inbox'
import { resetTransfers } from '@/mock/transfer-handlers'
import { createZip, textFile } from '@/mock/zip'
import { inboxPollMs } from '@/queries/inbox'
import { useContextStore } from '@/stores/context'
import { saveBlob } from '@/utils/download'
import { mountScreen, settle } from '../../helpers'

// The Inbox page of a view (424-31) against the mock API: the lock below Contributor and of a 403, the two tabs with
// the count, what an item shows and offers, accept and dismiss with every answer of the API, the download of an
// export and the report of an import, Load more, the re-read every 20 seconds, and a switch to another view while the
// page is open. Ada owns acme.billing, contributes to acme.shipping and reads northwind.portal; their default views
// start with the five seeded items.

vi.mock('@/utils/download', () => ({ saveBlob: vi.fn() }))
// the navbar needs the dashboard shell; the stand-in shows its title and the buttons of the page
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
/** The inbox requests of a test, in their order. */
const requests: { method: string; url: URL }[] = []
server.events.on('request:start', ({ request }) => {
  const url = new URL(request.url)
  if (url.pathname.includes('/inbox')) {
    requests.push({ method: request.method, url })
  }
})
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  resetTransfers()
  clearToasts()
  vi.useRealTimers()
  vi.mocked(saveBlob).mockClear()
  requests.length = 0
  document.body.innerHTML = ''
})
afterAll(() => server.close())

type Scope = { org: string; project: string; view: string }

const billing: Scope = { org: 'acme', project: 'billing', view: 'default' }
const shipping: Scope = { org: 'acme', project: 'shipping', view: 'default' }
const portal: Scope = { org: 'northwind', project: 'portal', view: 'default' }
/** A view of acme.billing that the mock does not seed. */
const empty: Scope = { org: 'acme', project: 'billing', view: 'empty' }

const accessMap = createSeed('default').account!.AccessMap as Record<string, string>

// The seeded items, by their titles.
const suggestion = 'Generate a schema for 4 similar configurations'
const failedExport = 'Export failed'
const note = 'Schema suggestions are on for this view'
const finishedImport = 'Import finished'
const accepted = 'Generate a schema for 7 similar configurations'

const inboxUrl = '*/api/v1/:org/:project/:view/inbox'

async function mountInbox(scope: Scope = billing, options: { map?: Record<string, string>; tab?: 'done' } = {}) {
  const view = await mountScreen(
    RouterView,
    {},
    {
      path: `/${scope.org}/${scope.project}/${scope.view}/inbox${options.tab ? '?tab=done' : ''}`,
      scope,
      accessMap: options.map ?? accessMap,
      routes,
    },
  )
  mounted.push(view)
  await settle(6)
  return view
}

const text = () => document.body.textContent?.replace(/\s+/g, ' ') ?? ''
const rows = () => [...document.body.querySelectorAll<HTMLLIElement>('ul[aria-label$=" items"] > li')]
const titles = () => rows().map((row) => row.querySelector('.font-medium')?.textContent)
const rowWith = (title: string) => rows().find((row) => row.textContent?.includes(title))
const button = (name: string, root: ParentNode = document.body) =>
  [...root.querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === name || candidate.getAttribute('aria-label') === name,
  )
const tabs = () => [...document.body.querySelectorAll<HTMLButtonElement>('button[role="tab"]')]
/** The tabs as they read: the label, and the count of its badge after it. */
const tabLabels = () =>
  tabs().map((tab) =>
    ['label', 'trailingBadge']
      .map((slot) => tab.querySelector(`[data-slot="${slot}"]`)?.textContent?.trim())
      .filter(Boolean)
      .join(' '),
  )
const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')
// The toasts are global state of Nuxt UI: each test reads the toasts of its own actions, and clears them.
const toaster = () => createApp({}).runWithContext(() => useToast())
const toasts = () =>
  toaster().toasts.value.map((toast) => (toast.description ? `${toast.title}: ${toast.description}` : toast.title))
const clearToasts = () => toaster().clear()
const listRequests = (state: 'Open' | 'Done') =>
  requests.filter((request) => request.method === 'GET' && request.url.searchParams.get('state') === state)
const posts = () =>
  requests.filter((request) => request.method === 'POST').map((request) => request.url.pathname.split('/').at(-1))

async function click(element: HTMLElement | undefined) {
  expect(element).toBeTruthy()
  element!.click()
  await settle()
}

async function openTab(name: 'Open' | 'Done') {
  const tab = tabs().find((candidate) => candidate.textContent?.trim().startsWith(name))
  expect(tab, name).toBeTruthy()
  tab!.dispatchEvent(new MouseEvent('mousedown', { button: 0, bubbles: true }))
  await settle()
}

/** Presses a button of the row with the title, as soon as the row is there. */
async function press(title: string, name: string) {
  await vi.waitFor(() => expect(button(name, rowWith(title))).toBeTruthy())
  await click(button(name, rowWith(title)))
}

/** Accepts the action with the title through its confirmation. */
async function accept(title: string) {
  await press(title, 'Accept')
  await vi.waitFor(() => expect(dialog()?.textContent).toContain('Accept this action?'))
  await click(button('Accept', dialog()!))
}

/** Sets the mock user's role on a project in the stored database, which every route reads. */
function setRole(key: string, role: AccountRole) {
  const db = loadDb()
  db.account!.AccessMap![key] = role
  saveDb(db)
}

/** Pushes items into the stored inbox of a view, as a producer of the back end would. */
function push(scope: Scope, ...pushes: InboxPush[]): StoredInboxItem[] {
  const db = loadDb()
  const items = pushes.map((item) => pushInboxItem(db, scope, item))
  saveDb(db)
  return items
}

const stored = (scope: Scope, title: string) => {
  const db = loadDb()
  return inboxOf(db, scope).find((item) => item.Title === title)!
}

const message = (title: string, more: Partial<InboxPush> = {}): InboxPush => ({
  Kind: 'Message',
  Topic: 'Test',
  Title: title,
  MinimalRole: 'Contributor',
  CreatedBy: systemActor,
  ...more,
})

/** An export of the view that ended, as a pipeline or another tab would leave it; the mock ends a job on its second read. */
async function exportEnded(scope: Scope = billing) {
  const { org, project, view } = scope
  const started = await startViewExport(org, project, view, { Format: 'Folder', Include: ['Annotations'] })
  await getViewExport(org, project, view, started.Id!)
  return getViewExport(org, project, view, started.Id!)
}

/** An import of the view that ended. */
async function importEnded(scope: Scope = billing) {
  const { org, project, view } = scope
  const archive = new File([createZip([textFile('annotations/rst.invoicing.json', '{"Title":"Edited"}')])], 'edited.zip')
  const started = await startViewImport(org, project, view, archive, { fileName: 'edited.zip' })
  await getViewImport(org, project, view, started.Id!)
  return getViewImport(org, project, view, started.Id!)
}

const refusal = (status: number, errorCode: string, messageText: string) =>
  HttpResponse.json({ Message: messageText, ErrorCode: errorCode }, { status })

type Mounted = Awaited<ReturnType<typeof mountInbox>>

/** The row component of the item with the title: what it emits is a press that reaches the page whatever the button. */
const rowOf = (view: Mounted, title: string) =>
  view.host.findAllComponents(InboxItemRow).find((row) => row.props('item').Title === title)!

/** A request that a handler keeps waiting until the test lets it go. */
function hold() {
  let release: () => void = () => {}
  const released = new Promise<void>((resolve) => (release = resolve))
  return { released, release }
}

const archiveUrl = '*/api/v1/:org/:project/:view/exports/:id/archive'

describe('Inbox page', () => {
  it('lists the open items of the view newest first, with the count on the Open tab', async () => {
    await mountInbox()

    await vi.waitFor(() => expect(titles()).toEqual([suggestion, failedExport, note]))
    expect(document.body.querySelector('[data-navbar]')?.getAttribute('data-navbar')).toBe('Inbox')
    expect(tabLabels()).toEqual(['Open 3', 'Done'])
    expect(tabs()[0]!.getAttribute('aria-selected')).toBe('true')
    // always with an explicit state, which is the key the menu shares
    expect(requests.map((request) => [request.method, request.url.pathname, request.url.search])).toEqual([
      ['GET', '/api/v1/acme/billing/default/inbox', '?state=Open'],
    ])
    // the names of people are kept out of PostHog
    expect(document.body.querySelector('ul[aria-label="Open items"]')?.classList).toContain('ph-no-capture')
  })

  it('shows what an item is, who caused it and what it offers', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))

    const action = rowWith(suggestion)!
    expect(action.textContent).toContain('4 configurations of this view have the same members.')
    expect(action.textContent).toMatch(/25 minutes ago by System/)
    expect(action.textContent).not.toContain('Administrators only')
    expect([...action.querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())).toEqual([
      'Accept',
      'Dismiss',
    ])

    // the failed job of another Administrator: its error text, and nothing that needs the job
    const failed = rowWith(failedExport)!
    expect(failed.textContent).toContain('Administrators only')
    expect(failed.textContent).toContain('TooManyRequests (429)')
    expect(failed.textContent).toMatch(/3 hours ago by Grace Hopper/)
    expect([...failed.querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())).toEqual([
      'Dismiss',
    ])

    expect([...rowWith(note)!.querySelectorAll('button')].map((candidate) => candidate.textContent?.trim())).toEqual([
      'Dismiss',
    ])
  })

  it('lists the accepted and dismissed items under Done, with who closed them, and reads them only then', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    expect(listRequests('Done')).toHaveLength(0)

    await openTab('Done')

    await vi.waitFor(() => expect(titles()).toEqual([finishedImport, accepted]))
    expect(listRequests('Done')).toHaveLength(1)
    expect(tabs()[1]!.getAttribute('aria-selected')).toBe('true')
    // the count stays the one of the open items
    expect(tabLabels()).toEqual(['Open 3', 'Done'])
    expect(rowWith(finishedImport)!.textContent).toMatch(/Dismissed by Grace Hopper, 4 hours ago/)
    expect(rowWith(accepted)!.textContent).toMatch(/Accepted by Ada Lovelace, 2 days ago/)
    // the import of the seed names no job, so it has no result to show
    expect(rows().flatMap((row) => [...row.querySelectorAll('button')])).toEqual([])
    expect(document.body.querySelector('ul[aria-label="Done items"]')).not.toBeNull()
  })

  it('opens on Done from its address', async () => {
    await mountInbox(billing, { tab: 'done' })

    await vi.waitFor(() => expect(titles()).toEqual([finishedImport, accepted]))
    expect(tabs()[1]!.getAttribute('aria-selected')).toBe('true')
    // the open items are read whatever the tab: the count comes from them
    expect(listRequests('Open')).toHaveLength(1)
    expect(tabLabels()).toEqual(['Open 3', 'Done'])
  })

  it('shows a Contributor the items of Contributors only', async () => {
    await mountInbox(shipping)

    await vi.waitFor(() => expect(titles()).toEqual([suggestion, note]))
    expect(tabLabels()).toEqual(['Open 2', 'Done'])
    expect(text()).not.toContain('Administrators only')

    await openTab('Done')
    await vi.waitFor(() => expect(titles()).toEqual([accepted]))
  })

  it.each([
    ['an Administrator', empty, 'Results of exports and imports and suggested actions of this view arrive here.'],
    ['a Contributor', { ...shipping, view: 'empty' }, 'Suggested actions of this view arrive here.'],
  ] as const)('tells %s what arrives in an inbox with nothing to do, without a count', async (_, scope, description) => {
    await mountInbox(scope)

    await vi.waitFor(() => expect(text()).toContain('Nothing to do'))
    expect(text()).toContain(description)
    expect(tabLabels()).toEqual(['Open', 'Done'])
    expect(rows()).toEqual([])

    await openTab('Done')
    await vi.waitFor(() => expect(text()).toContain('Nothing done yet'))
    expect(text()).not.toContain('Nothing to do')
  })

  it('shows a Reader the lock of Contributors and asks for nothing', async () => {
    await mountInbox(portal)

    expect(text()).toContain('Contributors only')
    expect(tabs()).toEqual([])
    expect(rows()).toEqual([])
    expect(button('Refresh')).toBeUndefined()
    expect(requests).toEqual([])
  })

  it('shows the same lock when the list answers 403, because the role was lost meanwhile', async () => {
    // the session still says Contributor; the API reads the role again and refuses
    await mountInbox(portal, { map: { ...accessMap, 'northwind.portal': 'Contributor' } })

    await vi.waitFor(() => expect(text()).toContain('Contributors only'))
    expect(listRequests('Open')).toHaveLength(1)
    expect(tabs()).toEqual([])
    expect(rows()).toEqual([])
    expect(text()).not.toContain('No access')
    expect(button('Retry')).toBeUndefined()

    // with the role back, Refresh opens the inbox again
    setRole('northwind.portal', 'Contributor')
    await click(button('Refresh'))

    await vi.waitFor(() => expect(titles()).toEqual([suggestion, note]))
    expect(text()).not.toContain('Contributors only')
  })

  it.each([
    ['a deployment without the routes', 404, 'NotFound', 'Not found'],
    ['a failing API', 500, 'InternalError', 'The API failed'],
  ] as const)('shows the list of %s as an error with Retry', async (_, status, code, title) => {
    let fail = true
    server.use(
      http.get(inboxUrl, () => (fail ? refusal(status, code, 'The inbox could not be read.') : undefined)),
    )
    await mountInbox()

    await vi.waitFor(() => expect(button('Retry')).toBeTruthy())
    expect(text()).toContain(title)
    expect(text()).toContain('The inbox could not be read.')
    expect(rows()).toEqual([])
    // no count from an answer that failed
    expect(tabLabels()).toEqual(['Open', 'Done'])

    fail = false
    await click(button('Retry'))

    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    expect(tabLabels()).toEqual(['Open 3', 'Done'])
  })

  it('reads both lists again with Refresh: the shown one at once, the other when its tab opens', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    await openTab('Done')
    await vi.waitFor(() => expect(rows()).toHaveLength(2))
    await openTab('Open')
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    // somebody else dismisses the note
    await dismissViewInboxItem(billing.org, billing.project, billing.view, stored(billing, note).Id)
    requests.length = 0

    await click(button('Refresh'))

    await vi.waitFor(() => expect(titles()).toEqual([suggestion, failedExport]))
    expect(tabLabels()).toEqual(['Open 2', 'Done'])
    expect(listRequests('Open')).toHaveLength(1)
    expect(listRequests('Done')).toHaveLength(0)

    await openTab('Done')
    await vi.waitFor(() => expect(titles()).toEqual([finishedImport, note, accepted]))
    expect(listRequests('Done')).toHaveLength(1)
  })
})

describe('accepting an action', () => {
  it('asks first, naming the action and that its work runs in the background, and does nothing on Cancel', async () => {
    await mountInbox()

    await press(suggestion, 'Accept')

    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Accept this action?'))
    expect(dialog()!.textContent).toContain(
      `${suggestion}: the work starts now and runs in the background. Its result arrives in this inbox.`,
    )
    await click(button('Cancel', dialog()!))

    await settle()
    expect(posts()).toEqual([])
    expect(titles()).toEqual([suggestion, failedExport, note])
    expect(stored(billing, suggestion).State).toBe('Open')
  })

  it('accepts it after the confirmation: the result arrives, and the action is under Done', async () => {
    await mountInbox()

    await accept(suggestion)

    // the mock runs the action at once, so its notification is in the fresh list
    await vi.waitFor(() => expect(titles()).toEqual(['Schema generation finished', failedExport, note]))
    expect(posts()).toEqual(['accept'])
    expect(toasts()).toEqual(['Action accepted: Its work runs in the background; the result arrives in this inbox.'])
    expect(tabLabels()).toEqual(['Open 3', 'Done'])
    expect(rowWith('Schema generation finished')!.textContent).toContain('A schema with 6 properties was generated')

    await openTab('Done')
    await vi.waitFor(() => expect(titles()).toEqual([suggestion, finishedImport, accepted]))
    expect(rowWith(suggestion)!.textContent).toMatch(/Accepted by Ada Lovelace, just now/)
    expect(button('Accept', rowWith(suggestion))).toBeUndefined()
  })

  it('says that the item was already handled when somebody closed it first (409 InboxItemClosed)', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    // another Administrator dismisses the action while this page still shows it
    await dismissViewInboxItem(billing.org, billing.project, billing.view, stored(billing, suggestion).Id)

    await accept(suggestion)

    await vi.waitFor(() =>
      expect(toasts()).toEqual(['The item was already handled: The inbox item was already dismissed.']),
    )
    // a fresh list, without the item
    await vi.waitFor(() => expect(titles()).toEqual([failedExport, note]))
    expect(tabLabels()).toEqual(['Open 2', 'Done'])
  })

  it("shows the API's message when the action could not start, and keeps the item open (the handler's own 409)", async () => {
    server.use(
      http.post(`${inboxUrl}/:id/accept`, () =>
        refusal(409, 'TransferLimit', 'The project already runs 3 exports and imports; wait for one to end.'),
      ),
    )
    await mountInbox()

    await accept(suggestion)

    await vi.waitFor(() =>
      expect(toasts()).toEqual([
        'The action could not start: The project already runs 3 exports and imports; wait for one to end.',
      ]),
    )
    // the list was read again, and the item is still open in it
    await vi.waitFor(() => expect(listRequests('Open')).toHaveLength(2))
    expect(titles()).toEqual([suggestion, failedExport, note])
    expect(button('Accept', rowWith(suggestion))!.disabled).toBe(false)
  })

  it('says that the action is not available when the deployment has no handler for it (501)', async () => {
    // an action whose code the mock does not run, as every action of the API today
    push(billing, message('Rebuild the search index', { Kind: 'Action', Topic: 'Search', Action: 'RebuildIndex' }))
    await mountInbox()

    await accept('Rebuild the search index')

    await vi.waitFor(() => expect(toasts()).toEqual(['This action is not available in this deployment']))
    expect(posts()).toEqual(['accept'])
    // the item stays open
    expect(titles()).toContain('Rebuild the search index')
    expect(stored(billing, 'Rebuild the search index').State).toBe('Open')
  })

  it('says that the item is gone when it is no longer found (404)', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    server.use(
      http.post(`${inboxUrl}/:id/accept`, ({ params }) =>
        refusal(404, 'NotFound', `The inbox item '${String(params.id)}' does not exist or has expired.`),
      ),
    )

    await accept(suggestion)

    await vi.waitFor(() =>
      expect(toasts()).toEqual(['The item is gone: It has expired, or your role no longer sees it.']),
    )
    await vi.waitFor(() => expect(listRequests('Open')).toHaveLength(2))
  })
})

describe('dismissing an item', () => {
  it('dismisses it without a question, and it is under Done, dismissed by the caller', async () => {
    await mountInbox()

    await press(note, 'Dismiss')

    await vi.waitFor(() => expect(titles()).toEqual([suggestion, failedExport]))
    expect(dialog()).toBeNull()
    expect(toasts()).toEqual([])
    expect(posts()).toEqual(['dismiss'])
    expect(tabLabels()).toEqual(['Open 2', 'Done'])

    await openTab('Done')
    await vi.waitFor(() => expect(titles()).toEqual([finishedImport, note, accepted]))
    expect(rowWith(note)!.textContent).toMatch(/Dismissed by Ada Lovelace, just now/)
  })

  it('takes no second dismiss and no accept of an item while its dismiss is on its way', async () => {
    const dismissal = hold()
    server.use(
      http.post(`${inboxUrl}/:id/dismiss`, async () => {
        await dismissal.released
        return undefined
      }),
    )
    const view = await mountInbox()

    await press(suggestion, 'Dismiss')
    await vi.waitFor(() => expect(posts()).toEqual(['dismiss']))

    // the buttons of the item are off, the ones of the other items are not
    expect(button('Accept', rowWith(suggestion))!.disabled).toBe(true)
    expect(button('Dismiss', rowWith(suggestion))!.disabled).toBe(true)
    expect(button('Dismiss', rowWith(note))!.disabled).toBe(false)
    // neither a second click nor a press that was already on its way sends a request or asks to accept
    await click(button('Dismiss', rowWith(suggestion)))
    rowOf(view, suggestion).vm.$emit('dismiss')
    rowOf(view, suggestion).vm.$emit('accept')
    await settle()
    expect(dialog()).toBeNull()
    expect(posts()).toEqual(['dismiss'])

    dismissal.release()

    await vi.waitFor(() => expect(titles()).toEqual([failedExport, note]))
    // one dismiss, so nobody is told that the item they dismissed was already handled
    expect(posts()).toEqual(['dismiss'])
    expect(toasts()).toEqual([])
  })

  it('removes the count from the Open tab with the last open item', async () => {
    push(empty, message('The only one'))
    await mountInbox(empty)
    await vi.waitFor(() => expect(tabLabels()).toEqual(['Open 1', 'Done']))

    await press('The only one', 'Dismiss')

    await vi.waitFor(() => expect(text()).toContain('Nothing to do'))
    expect(tabLabels()).toEqual(['Open', 'Done'])
  })

  it('says that the item was already handled when somebody closed it first (409 InboxItemClosed)', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    await dismissViewInboxItem(billing.org, billing.project, billing.view, stored(billing, note).Id)

    await press(note, 'Dismiss')

    await vi.waitFor(() =>
      expect(toasts()).toEqual(['The item was already handled: The inbox item was already dismissed.']),
    )
    await vi.waitFor(() => expect(titles()).toEqual([suggestion, failedExport]))
  })

  it('says that the item is gone when the role no longer sees it (404), and reads the list again', async () => {
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    // the caller is a Contributor now: an Administrators' item is not found, as an unknown one
    setRole('acme.billing', 'Contributor')

    await press(failedExport, 'Dismiss')

    await vi.waitFor(() =>
      expect(toasts()).toEqual(['The item is gone: It has expired, or your role no longer sees it.']),
    )
    await vi.waitFor(() => expect(titles()).toEqual([suggestion, note]))
    expect(stored(billing, failedExport).State).toBe('Open')
  })

  it("shows the API's message for any other refusal, and keeps the item", async () => {
    server.use(
      http.post(`${inboxUrl}/:id/dismiss`, () => refusal(409, 'Conflict', 'The inbox item changed; try again.')),
    )
    await mountInbox()

    await press(note, 'Dismiss')

    await vi.waitFor(() =>
      expect(toasts()).toEqual(['The item was not dismissed: The inbox item changed; try again.']),
    )
    await vi.waitFor(() => expect(listRequests('Open')).toHaveLength(2))
    expect(titles()).toEqual([suggestion, failedExport, note])
  })
})

describe('the result of a job', () => {
  it('downloads the archive of an export that ended elsewhere, under the name of its job', async () => {
    const job = await exportEnded()
    await mountInbox()
    await vi.waitFor(() => expect(titles()[0]).toBe('Export finished'))
    expect(rowWith('Export finished')!.textContent).toMatch(/Expires (tomorrow|in 23 hours|in 24 hours)/)

    await press('Export finished', 'Download')

    await vi.waitFor(() => expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce())
    const [archive, fileName] = vi.mocked(saveBlob).mock.calls[0]!
    expect(archive).toBeInstanceOf(Blob)
    expect(fileName).toBe(job.FileName)
    expect(fileName).toMatch(/^export-acme-billing-default-\d{8}-\d{6}Z\.zip$/)
    // the item stays open: a download closes nothing
    expect(toasts()).toEqual([])
    expect(posts()).toEqual([])
    expect(titles()[0]).toBe('Export finished')
  })

  it('fetches the archive of another export while a slow one is still on its way', async () => {
    await exportEnded()
    await exportEnded()
    const slow = hold()
    /** The jobs whose archives were asked for, in their order; the first request is the slow one. */
    const asked: string[] = []
    server.use(
      http.get(archiveUrl, async ({ params }) => {
        if (asked.push(String(params.id)) === 1) {
          await slow.released
        }
        return undefined
      }),
    )
    const view = await mountInbox()
    await vi.waitFor(() => expect(titles().slice(0, 2)).toEqual(['Export finished', 'Export finished']))
    const download = (index: number) => button('Download', rows()[index])!

    await click(download(0))
    await vi.waitFor(() => expect(asked).toHaveLength(1))
    await click(download(1))

    // the second archive is saved, and its button is free again, while the first one is still being fetched
    await vi.waitFor(() => expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce())
    await vi.waitFor(() => expect(download(1).disabled).toBe(false))
    expect(download(0).disabled).toBe(true)
    expect(new Set(asked).size).toBe(2)
    // the slow one is not asked for a second time, by a click or by a press that was already on its way
    await click(download(0))
    view.host.findAllComponents(InboxItemRow)[0]!.vm.$emit('download')
    await settle()
    expect(asked).toHaveLength(2)

    slow.release()

    await vi.waitFor(() => expect(vi.mocked(saveBlob)).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(download(0).disabled).toBe(false))
    expect(toasts()).toEqual([])
  })

  it('still offers the download once the item is done', async () => {
    await exportEnded()
    await dismissViewInboxItem(billing.org, billing.project, billing.view, stored(billing, 'Export finished').Id)
    await mountInbox(billing, { tab: 'done' })

    await press('Export finished', 'Download')

    await vi.waitFor(() => expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce())
  })

  it('says that the archive has expired when its job is gone (404)', async () => {
    await exportEnded()
    await mountInbox()
    await vi.waitFor(() => expect(titles()[0]).toBe('Export finished'))
    // the job is gone, as after its retention; the item that led to it is still listed
    resetTransfers()

    await press('Export finished', 'Download')

    await vi.waitFor(() =>
      expect(toasts()).toEqual([
        'The archive has expired: An export keeps its archive for a limited time. Export the view again.',
      ]),
    )
    expect(vi.mocked(saveBlob)).not.toHaveBeenCalled()
  })

  it('shows the report of an import in a wide slideover, without Import now', async () => {
    await importEnded()
    const view = await mountInbox()
    await vi.waitFor(() => expect(titles()[0]).toBe('Import finished'))

    await press('Import finished', 'Show the result')

    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Imported'))
    expect(dialog()!.className).toContain('max-w-5xl')
    // the title of the item, and the report under its own heading instead of "Latest import"
    expect(dialog()!.querySelector('h2')?.textContent).toBe('Import finished')
    expect(dialog()!.querySelector('#result-heading')?.textContent).toBe('Report')
    expect(dialog()!.textContent).not.toContain('Latest import')
    expect(dialog()!.textContent).toContain('edited.zip: 1 updated, 0 unchanged.')
    expect(dialog()!.textContent).toContain('annotations/rst.invoicing.json')
    expect(button('Import now', dialog()!)).toBeUndefined()
    expect(view.host.findComponent(ImportResult).props()).toMatchObject({ title: 'Report', canImportNow: false })
  })

  it('closes the slideover and says that the report has expired when its job is gone (404)', async () => {
    await importEnded()
    await mountInbox()
    await vi.waitFor(() => expect(titles()[0]).toBe('Import finished'))
    resetTransfers()

    await press('Import finished', 'Show the result')

    await vi.waitFor(() =>
      expect(toasts()).toEqual(['The report has expired: The report of an import is kept for a limited time.']),
    )
    await vi.waitFor(() => expect(dialog()).toBeNull())
  })

  it('shows another failure of the report in the slideover, with Retry', async () => {
    await importEnded()
    let fail = true
    server.use(
      http.get('*/api/v1/:org/:project/:view/imports/:id', () =>
        fail ? refusal(500, 'InternalError', 'The report could not be read.') : undefined,
      ),
    )
    await mountInbox()
    await vi.waitFor(() => expect(titles()[0]).toBe('Import finished'))

    await press('Import finished', 'Show the result')

    await vi.waitFor(() => expect(dialog()?.textContent).toContain('The report could not be read.'))
    expect(toasts()).toEqual([])

    fail = false
    await click(button('Retry', dialog()!))
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Imported'))
  })
})

describe('the list of a long inbox', () => {
  /** 55 messages more: with the three seeded open items, a first page of 50 and a second one of 8. */
  const fill = () => push(billing, ...Array.from({ length: 55 }, (_, index) => message(`Message ${index + 1}`)))

  it('appends the next page with Load more, with the same state, and stops after the last page', async () => {
    fill()
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(50))
    expect(tabLabels()).toEqual(['Open 58', 'Done'])

    await click(button('Load more'))

    await vi.waitFor(() => expect(rows()).toHaveLength(58))
    const next = requests.at(-1)!.url.searchParams
    expect(next.get('state')).toBe('Open')
    expect(next.get('continuationToken')).toBeTruthy()
    expect(button('Load more')).toBeUndefined()
    // no item twice, and the oldest one last
    expect(new Set(titles()).size).toBe(58)
    expect(titles().at(-1)).toBe(note)
  })

  it('keeps the loaded pages when a re-read answers the same first page', async () => {
    fill()
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(50))
    await click(button('Load more'))
    await vi.waitFor(() => expect(rows()).toHaveLength(58))
    requests.length = 0

    await click(button('Refresh'))

    await vi.waitFor(() => expect(listRequests('Open')).toHaveLength(1))
    await settle()
    expect(rows()).toHaveLength(58)
    expect(button('Load more')).toBeUndefined()
  })

  it('starts again from the first page after an item of the second page was dismissed', async () => {
    fill()
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(50))
    await click(button('Load more'))
    await vi.waitFor(() => expect(rows()).toHaveLength(58))
    expect(rows().findIndex((row) => row.textContent?.includes(note))).toBe(57)

    await press(note, 'Dismiss')

    // the fresh first page has another count, so the pages behind the old one are dropped
    await vi.waitFor(() => expect(tabLabels()).toEqual(['Open 57', 'Done']))
    expect(rows()).toHaveLength(50)
    expect(rowWith(note)).toBeUndefined()
    expect(button('Load more')).toBeTruthy()

    await click(button('Load more'))
    await vi.waitFor(() => expect(rows()).toHaveLength(57))
    expect(rowWith(note)).toBeUndefined()
  })

  it('drops a next page that arrives after the first page changed', async () => {
    fill()
    let release: () => void = () => {}
    const held = new Promise<void>((resolve) => (release = resolve))
    let isHeld = false
    let isAnswered = false
    server.use(
      http.get(inboxUrl, async ({ request }) => {
        if (new URL(request.url).searchParams.get('continuationToken')) {
          isHeld = true
          await held
          isAnswered = true
        }
        return undefined
      }),
    )
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(50))

    await click(button('Load more'))
    await vi.waitFor(() => expect(isHeld).toBe(true))
    // a new item arrives while the next page of the old first page is still loading
    push(billing, message('Arrived meanwhile'))
    await click(button('Refresh'))
    await vi.waitFor(() => expect(titles()[0]).toBe('Arrived meanwhile'))
    release()
    await vi.waitFor(() => expect(isAnswered).toBe(true))
    await settle()

    expect(rows()).toHaveLength(50)
    expect(button('Load more')!.disabled).toBe(false)
  })
})

describe('a switch to another view while the page is open', () => {
  /** As the switchers of the view and the project move the page: the context guard sets the scope, the page stays. */
  async function switchTo(view: Mounted, scope: Scope) {
    useContextStore().set(scope)
    await view.router.push(`/${scope.org}/${scope.project}/${scope.view}/inbox`)
    await settle()
  }

  it('shows the inbox of the other view: the report of the view before closes, and its late next page is dropped', async () => {
    // a minute old, so the import that ends now is the newest item of the first page of 50
    const earlier = new Date(Date.now() - 60_000).toISOString()
    push(billing, ...Array.from({ length: 55 }, (_, index) => message(`Message ${index + 1}`, { CreatedAt: earlier })))
    await importEnded()
    const next = hold()
    let isHeld = false
    let isAnswered = false
    server.use(
      http.get(inboxUrl, async ({ request }) => {
        if (new URL(request.url).searchParams.get('continuationToken')) {
          isHeld = true
          await next.released
          isAnswered = true
        }
        return undefined
      }),
    )
    const view = await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(50))
    await click(button('Load more'))
    await vi.waitFor(() => expect(isHeld).toBe(true))
    await press('Import finished', 'Show the result')
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Imported'))

    await switchTo(view, empty)

    await vi.waitFor(() => expect(text()).toContain('Nothing to do'))
    // the report belongs to the view before: with the scope of this one its links would lead to other documents
    await vi.waitFor(() => expect(dialog()).toBeNull())
    expect(rows()).toEqual([])
    expect(tabLabels()).toEqual(['Open', 'Done'])
    expect(requests.at(-1)!.url.pathname).toBe('/api/v1/acme/billing/empty/inbox')

    next.release()
    await vi.waitFor(() => expect(isAnswered).toBe(true))
    await settle()

    // the next page of the view before is not appended to the list of this one
    expect(rows()).toEqual([])
    expect(text()).toContain('Nothing to do')
    expect(button('Load more')).toBeUndefined()
    expect(toasts()).toEqual([])
  })

  it('shows the lock in a project where the caller is a Reader, and asks for nothing there', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const view = await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    const asked = () => requests.map((request) => request.url.pathname)
    expect(asked()).toEqual(['/api/v1/acme/billing/default/inbox'])

    await switchTo(view, portal)

    await vi.waitFor(() => expect(text()).toContain('Contributors only'))
    expect(tabs()).toEqual([])
    expect(rows()).toEqual([])
    expect(button('Refresh')).toBeUndefined()
    expect(asked()).toEqual(['/api/v1/acme/billing/default/inbox'])

    // the re-read of the open items stops with the role
    vi.advanceTimersByTime(inboxPollMs)
    await settle()
    expect(asked()).toEqual(['/api/v1/acme/billing/default/inbox'])
  })

  it('keeps the Download of an archive that is still being fetched off when the person comes back to the view', async () => {
    await exportEnded()
    const slow = hold()
    server.use(
      http.get(archiveUrl, async () => {
        await slow.released
        return undefined
      }),
    )
    const view = await mountInbox()
    await press('Export finished', 'Download')
    await vi.waitFor(() => expect(button('Download', rowWith('Export finished'))!.disabled).toBe(true))

    await switchTo(view, empty)
    await vi.waitFor(() => expect(text()).toContain('Nothing to do'))
    await switchTo(view, billing)

    await vi.waitFor(() => expect(rowWith('Export finished')).toBeTruthy())
    expect(button('Download', rowWith('Export finished'))!.disabled).toBe(true)
    expect(vi.mocked(saveBlob)).not.toHaveBeenCalled()

    slow.release()

    await vi.waitFor(() => expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce())
    await vi.waitFor(() => expect(button('Download', rowWith('Export finished'))!.disabled).toBe(false))
  })
})

describe('the freshness of the page', () => {
  // only the interval is faked: the requests and their answers keep the real clock
  const fakeInterval = () => vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })

  it('reads the open items again every 20 seconds, whatever the tab, and the Done list only for its tab', async () => {
    expect(inboxPollMs).toBe(20_000)
    fakeInterval()
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    expect(listRequests('Open')).toHaveLength(1)

    vi.advanceTimersByTime(inboxPollMs / 2)
    await settle()
    expect(listRequests('Open')).toHaveLength(1)

    // an item arrives; the next read shows it and counts it
    push(billing, message('Arrived meanwhile'))
    vi.advanceTimersByTime(inboxPollMs / 2)
    await vi.waitFor(() => expect(titles()[0]).toBe('Arrived meanwhile'))
    expect(listRequests('Open')).toHaveLength(2)
    expect(tabLabels()).toEqual(['Open 4', 'Done'])

    await openTab('Done')
    await vi.waitFor(() => expect(rows()).toHaveLength(2))
    push(billing, message('Arrived under Done'))
    vi.advanceTimersByTime(inboxPollMs)

    await vi.waitFor(() => expect(tabLabels()).toEqual(['Open 5', 'Done']))
    expect(listRequests('Open')).toHaveLength(3)
    expect(listRequests('Done')).toHaveLength(1)
  })

  it('stops reading while the list is in error, and starts again after Retry', async () => {
    fakeInterval()
    let fail = false
    server.use(
      http.get(inboxUrl, () => (fail ? refusal(500, 'InternalError', 'The inbox could not be read.') : undefined)),
    )
    await mountInbox()
    await vi.waitFor(() => expect(rows()).toHaveLength(3))

    fail = true
    vi.advanceTimersByTime(inboxPollMs)
    await vi.waitFor(() => expect(button('Retry')).toBeTruthy())
    expect(listRequests('Open')).toHaveLength(2)
    // the count of the answer before is not shown next to the error
    expect(tabLabels()).toEqual(['Open', 'Done'])

    vi.advanceTimersByTime(3 * inboxPollMs)
    await settle()
    expect(listRequests('Open')).toHaveLength(2)

    fail = false
    await click(button('Retry'))
    await vi.waitFor(() => expect(rows()).toHaveLength(3))
    expect(listRequests('Open')).toHaveLength(3)

    vi.advanceTimersByTime(inboxPollMs)
    await vi.waitFor(() => expect(listRequests('Open')).toHaveLength(4))
  })
})
