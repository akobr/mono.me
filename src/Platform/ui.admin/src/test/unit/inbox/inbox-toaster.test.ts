/* eslint-disable vue/one-component-per-file -- the toaster is mounted next to the menu in a small host */
import 'fake-indexeddb/auto'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { QueryClient } from '@tanstack/vue-query'
import { useToast } from '@nuxt/ui/composables'
import {
  getViewExport,
  getViewInbox,
  startViewExport,
  type AccountAccessMap,
  type ViewExport,
} from '@42for.net/2splatform.sdk'
import AppSidebar from '@/components/shell/AppSidebar.vue'
import TransferToaster from '@/components/transfer/TransferToaster.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb, mockUser, saveDb } from '@/mock/db'
import { exportNotification, inboxOf, pushInboxItem, type StoredInboxItem } from '@/mock/inbox'
import { resetTransfers } from '@/mock/transfer-handlers'
import { createZip, textFile } from '@/mock/zip'
import { dismissJobItem, jobItemRetryMs } from '@/queries/inbox'
import { useTransfersStore, type TransferJob } from '@/stores/transfers'
import { saveBlob } from '@/utils/download'
import { mountScreen, settle, type AppMountOptions } from '../../helpers'

// What connects the transfers to the inbox (424-31, spec 7.4). The back end leaves the end of every export and import
// in the inbox of its view. The tab that followed a job has shown that end already, so TransferToaster dismisses the
// job's item: it is under Done, and the mark of the menu does not light for it. What this tab did not show stays open.

vi.mock('@/utils/download', () => ({ saveBlob: vi.fn() }))

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
const staging: Scope = { org: 'acme', project: 'billing', view: 'staging' }
/** A view of acme.billing that the mock does not seed: its inbox starts empty. */
const empty: Scope = { org: 'acme', project: 'billing', view: 'empty' }

const accessMap = createSeed('default').account!.AccessMap as Record<string, string>
const inboxUrl = '*/api/v1/:org/:project/:view/inbox'

// The toasts are global state of Nuxt UI: each test reads the toasts of its own jobs, and clears them.
const toaster = () => createApp({}).runWithContext(() => useToast())
const toasts = () => toaster().toasts.value.map((toast) => toast.title)
const clearToasts = () => toaster().clear()

const posts = () =>
  requests.filter((request) => request.method === 'POST').map((request) => request.url.pathname.split('/').at(-1))
const reads = (scope: Scope) =>
  requests.filter(
    (request) =>
      request.method === 'GET' && request.url.pathname === `/api/v1/${scope.org}/${scope.project}/${scope.view}/inbox`,
  )

/** The stored item of a job, whatever its state. */
function itemOf(scope: Scope, jobId: string | undefined): StoredInboxItem | undefined {
  const db = loadDb()
  return inboxOf(db, scope).find((item) => item.Job?.Id === jobId)
}

/** An export of the view that ended without this tab, as a pipeline or another tab leaves it. */
async function exportEnded(scope: Scope): Promise<ViewExport> {
  const { org, project, view } = scope
  const started = await startViewExport(org, project, view, { Format: 'Folder', Include: ['Annotations'] })
  await getViewExport(org, project, view, started.Id!)
  return getViewExport(org, project, view, started.Id!)
}

const zip = () =>
  new File([createZip([textFile('templates/exe.template.json', '{"retries": 7}')])], 'template.zip', {
    type: 'application/zip',
  })

async function mountToaster(scope: Scope = billing) {
  const view = await mountScreen(TransferToaster, {}, { scope, accessMap, account: { Id: mockUser.id, AccessMap: accessMap as AccountAccessMap } })
  mounted.push(view)
  return view
}

const ended = (job: TransferJob) =>
  vi.waitFor(() => expect(['done', 'failed']).toContain(job.phase), { timeout: 10_000 })

describe('TransferToaster and the inbox', () => {
  it('dismisses the item of an export it reported, which is then under Done with its archive', async () => {
    await mountToaster()
    const job = await useTransfersStore().startExport(billing, { format: 'Folder' })
    await ended(job)

    await vi.waitFor(() => expect(itemOf(billing, job.id)?.State).toBe('Dismissed'))
    const item = itemOf(billing, job.id)!
    expect(item).toMatchObject({
      Title: 'Export finished',
      Closed: { By: { Kind: 'Account', Id: 'user_01MOCKADA', Name: 'Ada Lovelace' } },
      Job: { Kind: 'ViewExport', Id: job.id, FileName: job.state.FileName },
    })
    expect(posts()).toEqual(['dismiss'])
    // the tab reported the job once, as before: the toast and the download
    expect(toasts()).toHaveLength(1)
    expect(toasts()[0]).toMatch(/^Exported /)
    expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce()

    const done = await getViewInbox(billing.org, billing.project, billing.view, { state: 'Done' })
    expect(done.Items?.[0]).toMatchObject({ Id: item.Id, State: 'Dismissed', Job: { Id: job.id } })
  })

  it('dismisses the item of an import too, whether a page showed its end or the toast did', async () => {
    await mountToaster()
    const transfers = useTransfersStore()
    const shown = await transfers.startImport(billing, zip(), { dryRun: true })
    const release = transfers.showInPage(shown.id)
    const reported = await transfers.startImport(staging, zip())
    await ended(shown)
    await ended(reported)

    await vi.waitFor(() => expect(itemOf(billing, shown.id)?.State).toBe('Dismissed'))
    await vi.waitFor(() => expect(itemOf(staging, reported.id)?.State).toBe('Dismissed'))
    expect(itemOf(billing, shown.id)!.Title).toBe('Check finished')
    expect(itemOf(staging, reported.id)!.Title).toBe('Import finished')
    // only the import no page showed got a toast
    expect(toasts()).toEqual(['Imported template.zip'])
    release()
  })

  it('leaves the items of jobs this tab did not follow open: of another view, and of the same view', async () => {
    const elsewhere = await exportEnded(staging)
    const pipeline = await exportEnded(billing)
    await mountToaster()
    const job = await useTransfersStore().startExport(billing, { format: 'Folder' })
    await ended(job)

    await vi.waitFor(() => expect(itemOf(billing, job.id)?.State).toBe('Dismissed'))
    await settle()
    expect(itemOf(staging, elsewhere.Id)?.State).toBe('Open')
    expect(itemOf(billing, pipeline.Id)?.State).toBe('Open')
    expect(posts()).toEqual(['dismiss'])
    // the inbox of the other view is not even read
    expect(reads(staging)).toEqual([])
  })

  it('dismisses the item of a job that failed on the server, which the toast reported', async () => {
    await mountToaster()
    const transfers = useTransfersStore()
    let failed: ViewExport | undefined
    // the mock never fails a job: the second read of this one answers Failed, and leaves the item the API pushes
    server.use(
      http.get('*/api/v1/:org/:project/:view/exports/:id', ({ params }) => {
        const job = transfers.find(String(params.id))
        if (!job || job.state.Status !== 'Running') {
          return undefined
        }
        failed = {
          ...job.state,
          Status: 'Failed',
          Error: 'The storage did not answer.',
          CompletedAt: new Date().toISOString(),
        }
        const db = loadDb()
        pushInboxItem(db, billing, exportNotification(failed))
        saveDb(db)
        return HttpResponse.json(failed)
      }),
    )

    const job = await transfers.startExport(billing, { format: 'Folder' })
    await ended(job)

    expect(job.phase).toBe('failed')
    await vi.waitFor(() => expect(itemOf(billing, job.id)?.State).toBe('Dismissed'))
    expect(itemOf(billing, job.id)!.Title).toBe('Export failed')
    expect(toasts()).toEqual(['The export failed'])
  })

  it('leaves the item open when the tab could not download the archive: the inbox still leads to it', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/:view/exports/:id/archive', () =>
        HttpResponse.json({ Message: 'The archive could not be read.', ErrorCode: 'InternalError' }, { status: 500 }),
      ),
    )
    const view = await mountToaster()
    const invalidated = vi.spyOn(view.queryClient, 'invalidateQueries')
    const job = await useTransfersStore().startExport(billing, { format: 'Folder' })
    await ended(job)

    expect(job.phase).toBe('failed')
    expect(job.error).toContain('The archive could not be downloaded')
    await vi.waitFor(() =>
      expect(invalidated.mock.calls.map(([filters]) => (filters as { queryKey: string[] }).queryKey)).toEqual([
        ['v1', 'acme', 'billing', 'default', 'inbox'],
      ]),
    )
    await settle()
    expect(itemOf(billing, job.id)).toMatchObject({ Title: 'Export finished', State: 'Open' })
    expect(requests).toEqual([])
  })

  it('reads the inbox of the view again once the item is dismissed, and of no other view', async () => {
    const view = await mountToaster()
    const invalidated = vi.spyOn(view.queryClient, 'invalidateQueries')
    const job = await useTransfersStore().startExport(billing, { format: 'Folder' })
    await ended(job)

    await vi.waitFor(() =>
      expect(invalidated.mock.calls.map(([filters]) => (filters as { queryKey: string[] }).queryKey)).toEqual([
        ['v1', 'acme', 'billing', 'default', 'inbox'],
      ]),
    )
    // by then the item is closed, so the fresh lists have it under Done
    expect(itemOf(billing, job.id)?.State).toBe('Dismissed')
  })

  it.each([
    ['fails', 500, 'InternalError'],
    ['has no inbox routes yet', 404, 'NotFound'],
    ['refuses the caller', 403, 'AccessDenied'],
  ] as const)('reports the job as before and says nothing more when the inbox %s', async (_, status, errorCode) => {
    server.use(
      http.all(`${inboxUrl}*`, () =>
        HttpResponse.json({ Message: 'The inbox could not be read.', ErrorCode: errorCode }, { status }),
      ),
    )
    await mountToaster()
    const job = await useTransfersStore().startExport(billing, { format: 'Folder' })
    await ended(job)

    expect(job.phase).toBe('done')
    await vi.waitFor(() => expect(reads(billing)).toHaveLength(1))
    await settle()
    expect(toasts()).toHaveLength(1)
    expect(toasts()[0]).toMatch(/^Exported /)
    expect(posts()).toEqual([])
    expect(vi.mocked(saveBlob)).toHaveBeenCalledOnce()
  })

  it('does not light the mark of the menu for a job the tab reported, and lights it for one it did not', async () => {
    const Shell = defineComponent({ setup: () => () => [h(AppSidebar), h(TransferToaster)] })
    const view = await mountScreen(Shell, {}, {
      scope: empty as AppMountOptions['scope'],
      accessMap,
      account: { Id: mockUser.id, AccessMap: accessMap as AccountAccessMap },
    })
    mounted.push(view)
    const dot = () => document.body.querySelector('a[href$="/empty/inbox"] [data-slot="linkLeadingChip"]')
    await vi.waitFor(() => expect(reads(empty)).toHaveLength(1))
    expect(dot()).toBeNull()

    const job = await useTransfersStore().startExport(empty, { format: 'Folder' })
    await ended(job)
    await vi.waitFor(() => expect(itemOf(empty, job.id)?.State).toBe('Dismissed'))
    // the menu reads the open items again after the dismissal: there are none
    await vi.waitFor(() => expect(reads(empty).length).toBeGreaterThan(2))
    await settle()
    expect(dot()).toBeNull()

    // an export that ended in another tab; this one hears of it with the next read of the inbox
    await exportEnded(empty)
    await view.queryClient.invalidateQueries({ queryKey: ['v1', 'acme', 'billing', 'empty', 'inbox'] })
    await vi.waitFor(() => expect(dot()).not.toBeNull())
  })
})

describe('dismissJobItem', () => {
  /** Only the wait between the two looks is faked: the requests keep the real clock. */
  const fakeWait = () => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  /** Lets the requests on their way answer, without moving the faked clock. */
  const answered = async (condition: () => boolean) => {
    for (let round = 0; round < 100 && !condition(); round++) {
      await settle(1)
    }
    expect(condition()).toBe(true)
  }

  it('looks once more after two seconds for an item that is not there yet, and dismisses it then', async () => {
    expect(jobItemRetryMs).toBe(2000)
    const job = await exportEnded(billing)
    let looks = 0
    // the first look comes before the back end has pushed the item
    server.use(
      http.get(inboxUrl, () => (++looks === 1 ? HttpResponse.json({ Items: [], Count: 0, OpenCount: 0 }) : undefined)),
    )
    fakeWait()

    let isDone = false
    const dismissal = dismissJobItem(new QueryClient(), billing, job.Id!, mockUser.id)
    void dismissal.then(() => (isDone = true))
    await answered(() => looks === 1)

    await vi.advanceTimersByTimeAsync(jobItemRetryMs - 1)
    await settle()
    expect(looks).toBe(1)
    expect(itemOf(billing, job.Id)?.State).toBe('Open')

    await vi.advanceTimersByTimeAsync(1)
    await answered(() => isDone)
    await dismissal
    expect(looks).toBe(2)
    expect(itemOf(billing, job.Id)?.State).toBe('Dismissed')
    expect(requests.map((request) => [request.method, request.url.search])).toEqual([
      ['GET', '?state=Open'],
      ['GET', '?state=Open'],
      ['POST', ''],
    ])
  })

  it('gives up after the second look, without an error', async () => {
    await exportEnded(billing)
    fakeWait()

    let isDone = false
    const dismissal = dismissJobItem(new QueryClient(), billing, 'no-such-job', mockUser.id)
    void dismissal.then(() => (isDone = true))
    await answered(() => reads(billing).length === 1)
    await vi.advanceTimersByTimeAsync(jobItemRetryMs)
    await answered(() => isDone)

    await expect(dismissal).resolves.toBeUndefined()
    expect(reads(billing)).toHaveLength(2)
    expect(posts()).toEqual([])
  })

  it('dismisses nothing but the notification of the job, also when another item names the same job', async () => {
    const job = await exportEnded(billing)
    // an accepted action may carry the job its acceptance started; here it is still open, and not the job's result
    const db = loadDb()
    const action = pushInboxItem(db, billing, {
      Kind: 'Action',
      Topic: 'ViewExport',
      Title: 'Export the view again',
      MinimalRole: 'Administrator',
      CreatedBy: { Kind: 'System', Id: 'system' },
      Action: 'ExportView',
      Job: { Kind: 'ViewExport', Id: job.Id },
    })
    saveDb(db)

    await dismissJobItem(new QueryClient(), billing, job.Id!, mockUser.id)

    const items = inboxOf(loadDb(), billing)
    expect(items.find((item) => item.Id === action.Id)?.State).toBe('Open')
    expect(items.find((item) => item.Title === 'Export finished')?.State).toBe('Dismissed')
    expect(posts()).toEqual(['dismiss'])
  })

  it('dismisses nothing that another account started, and nothing without an account', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout'] })
    const job = await exportEnded(billing)

    // the item is shared by everybody who sees it, so a dismissal stamped with another person is never made for them
    const other = dismissJobItem(new QueryClient(), billing, job.Id!, 'user_02OTHER')
    await vi.advanceTimersByTimeAsync(jobItemRetryMs)
    await other
    await dismissJobItem(new QueryClient(), billing, job.Id!, undefined)

    expect(inboxOf(loadDb(), billing).find((item) => item.Title === 'Export finished')?.State).toBe('Open')
    expect(posts()).toEqual([])
    vi.useRealTimers()
  })

  it('resolves when the dismissal itself is refused, because somebody closed the item first', async () => {
    const job = await exportEnded(billing)
    server.use(
      http.post(`${inboxUrl}/:id/dismiss`, () =>
        HttpResponse.json(
          { Message: 'The inbox item was already dismissed.', ErrorCode: 'InboxItemClosed' },
          { status: 409 },
        ),
      ),
    )

    await expect(dismissJobItem(new QueryClient(), billing, job.Id!, mockUser.id)).resolves.toBeUndefined()
    expect(posts()).toEqual(['dismiss'])
  })
})
