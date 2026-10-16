import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { toRaw } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  getViewExport,
  getViewExportArchive,
  getViewImport,
  hasErrorCode,
  startViewExport,
  startViewImport,
  type StorytellerError,
  type ViewArchiveContent,
  type ViewImportExisting,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { resetTransfers } from '@/mock/transfer-handlers'
import { createZip, crc32, readZip, textFile } from '@/mock/zip'
import { maxWaitMs, useTransfersStore, type ExportJob, type ImportJob } from '@/stores/transfers'
import { importCounts } from '@/utils/transfers'
import { saveBlob } from '@/utils/download'

vi.mock('@/utils/download', () => ({ saveBlob: vi.fn() }))

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  server.events.removeAllListeners()
  resetTransfers()
  vi.useRealTimers()
})
afterAll(() => server.close())

const scope = { org: 'acme', project: 'billing', view: 'default' }

/** Runs the polling of the store until the job ends. */
async function settle(job: { phase: string }) {
  for (let step = 0; step < 50 && (job.phase === 'running' || job.phase === 'downloading'); step++) {
    await vi.advanceTimersByTimeAsync(3000)
  }
}

describe('mock zip', () => {
  it('writes stored zips that it reads back', async () => {
    const zip = createZip([textFile('rst.a.json', '{"a":1}\n'), textFile('sub/ünï.json', '{}')])

    const files = await readZip(zip)

    expect(files.map((file) => file.name)).toEqual(['rst.a.json', 'sub/ünï.json'])
    expect(new TextDecoder().decode(files[0]!.data)).toBe('{"a":1}\n')
  })

  it('reads deflated entries of a zip written by .NET', async () => {
    const fixture =
      'UEsDBBQAAAAIAOQmTl2U1LgqIQAAAI8AAAARAAAAcnN0LnBheW1lbnRzLmpzb26rVipKLSnKTC1WsjLUUcrNT0lVslJKSixJzlCgP6lUCwBQSwECFAAUAAAACADkJk5dlNS4KiEAAACPAAAAEQAAAAAAAAAAAAAAAAAAAAAAcnN0LnBheW1lbnRzLmpzb25QSwUGAAAAAAEAAQA/AAAAUAAAAAAA'
    const files = await readZip(Uint8Array.from(atob(fixture), (character) => character.charCodeAt(0)))

    expect(files).toHaveLength(1)
    expect(JSON.parse(new TextDecoder().decode(files[0]!.data))).toEqual({ retries: 1, mode: 'batch '.repeat(20) })
  })

  it('refuses what is not a zip', async () => {
    await expect(readZip(new TextEncoder().encode('not a zip at all, really not'))).rejects.toThrow('not a valid zip')
  })

  it('computes the CRC-32 of zlib', () => {
    expect(crc32(new TextEncoder().encode('123456789'))).toBe(0xcbf43926)
  })

  it('writes the time of each entry, or the time of the zip', () => {
    const zip = createZip(
      [textFile('a.json', '{}', new Date(2026, 9, 14, 13, 30, 12)), textFile('b.json', '{}')],
      new Date(2025, 0, 2, 3, 4, 6),
    )
    const view = new DataView(zip.buffer)
    // the DOS time and date of the local headers: a.json at 0, b.json after its 30 + 6 + 2 bytes
    const entryTime = (offset: number) => [view.getUint16(offset + 10, true), view.getUint16(offset + 12, true)]

    expect(entryTime(0)).toEqual([(13 << 11) | (30 << 5) | 6, (46 << 9) | (10 << 5) | 14])
    expect(entryTime(38)).toEqual([(3 << 11) | (4 << 5) | 3, (45 << 9) | (1 << 5) | 2])
  })
})

describe('importCounts', () => {
  it('lists the counts that are not zero', () => {
    expect(
      importCounts({ IsDryRun: false, Summary: { Created: 2, Updated: 0, Unchanged: 5, Skipped: 0, Failed: 1 } }),
    ).toBe('2 created, 5 unchanged, 1 failed')
    expect(importCounts({ IsDryRun: true, Summary: { Updated: 1 } })).toBe('1 to update, 0 unchanged')
  })
})

describe('transfers store', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'Date'] })
    vi.mocked(saveBlob).mockClear()
  })

  it('waits for an export, downloads it under its name and reports the end', async () => {
    const transfers = useTransfersStore()
    const finished: string[] = []
    transfers.onFinished((job) => finished.push(`${job.kind}:${job.phase}`))

    const job = await transfers.startExport(scope, { format: 'Git' })

    expect(job.phase).toBe('running')
    expect(JSON.parse(sessionStorage.getItem('2s-admin:pending-exports')!)).toEqual([
      { id: job.id, scope, startedAt: job.startedAt },
    ])

    await settle(job)

    expect(job.phase).toBe('done')
    expect(job.state.Status).toBe('Succeeded')
    expect(vi.mocked(saveBlob)).toHaveBeenCalledWith(expect.any(Blob), job.state.FileName)
    expect(job.state.FileName).toMatch(/^export-acme-billing-default-git-\d{8}-\d{6}Z\.zip$/)
    expect(finished).toEqual(['export:done'])
    expect(sessionStorage.getItem('2s-admin:pending-exports')).toBeNull()
  })

  it('reports a failed export with the error of the server', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/:view/exports/:id', () =>
        HttpResponse.json({ Id: 'x', Status: 'Failed', Error: 'The export failed: boom' }),
      ),
    )
    const transfers = useTransfersStore()

    const job = await transfers.startExport(scope, { format: 'Folder' })
    await settle(job)

    expect(job.phase).toBe('failed')
    expect(job.error).toBe('The export failed: boom')
    expect(vi.mocked(saveBlob)).not.toHaveBeenCalled()
  })

  it('stops at an expired job and after the waiting limit', async () => {
    server.use(
      http.get('*/api/v1/:org/:project/:view/exports/:id', () =>
        HttpResponse.json({ Message: 'gone', ErrorCode: 'NotFound' }, { status: 404 }),
      ),
    )
    const transfers = useTransfersStore()
    const expired = await transfers.startExport(scope, { format: 'Folder' })
    await settle(expired)
    expect(expired.error).toBe('The job has expired.')

    server.use(
      http.get('*/api/v1/:org/:project/:view/exports/:id', () => HttpResponse.json({ Id: 'y', Status: 'Running' })),
    )
    const slow = await transfers.startExport(scope, { format: 'Folder' })
    await vi.advanceTimersByTimeAsync(maxWaitMs + 10_000)
    expect(slow.phase).toBe('failed')
    expect(slow.error).toMatch(/did not end within 15 minutes/)
  })

  it('resumes the exports that ran before a reload', async () => {
    const first = useTransfersStore()
    const job = await first.startExport(scope, { format: 'Folder' })
    const pending = sessionStorage.getItem('2s-admin:pending-exports')

    // a reload: a new store reads what the previous page left
    const { createPinia, setActivePinia } = await import('pinia')
    setActivePinia(createPinia())
    sessionStorage.setItem('2s-admin:pending-exports', pending!)
    const second = useTransfersStore()
    second.resume()

    const resumed = second.find(job.id) as ExportJob
    expect(resumed.phase).toBe('running')
    await settle(resumed)
    expect(resumed.phase).toBe('done')
  })

  it('passes the options of an export and an import to the API, and leaves out the defaults', async () => {
    const requests: Request[] = []
    server.events.on('request:start', ({ request }) => {
      if (request.method === 'POST') {
        requests.push(request.clone())
      }
    })
    const transfers = useTransfersStore()
    const file = new File([createZip([textFile('rst.payments.json', '{}')])], 'view.zip', { type: 'application/zip' })

    const partial = await transfers.startExport(scope, { format: 'Git', include: ['Annotations', 'Templates'] })
    const all = await transfers.startExport(scope, { format: 'Folder', include: [] })
    const checked = await transfers.startImport(scope, file, {
      include: ['Schemas', 'Configurations'],
      existing: 'Skip',
      force: true,
      dryRun: true,
    })
    await transfers.startImport(scope, file, { existing: 'Overwrite', force: false, dryRun: false })
    const plain = await transfers.startImport(scope, file)

    const [partialRequest, allRequest, ...importRequests] = requests
    expect(await partialRequest!.json()).toEqual({ Format: 'Git', Include: ['Annotations', 'Templates'] })
    expect(await allRequest!.json()).toEqual({ Format: 'Folder' })
    expect(importRequests.map((request) => Object.fromEntries(new URL(request.url).searchParams))).toEqual([
      { include: 'Schemas,Configurations', existing: 'Skip', force: 'true', dryRun: 'true', fileName: 'view.zip' },
      { existing: 'Overwrite', fileName: 'view.zip' },
      { fileName: 'view.zip' },
    ])

    // the jobs keep what the server resolved
    expect(partial.state).toMatchObject({ Format: 'Git', Include: ['Annotations', 'Templates'] })
    expect(all.state.Include).toEqual(['Annotations', 'Schemas', 'Templates', 'Configurations'])
    expect(checked.state).toMatchObject({
      Include: ['Schemas', 'Configurations'],
      Existing: 'Skip',
      IsForced: true,
      IsDryRun: true,
    })
    expect(plain.state).toMatchObject({ Existing: 'Overwrite', IsForced: false, IsDryRun: false })
    // the uploaded file stays with the job, so a checked archive can be imported for real
    expect(toRaw(checked.file)).toBe(file)
  })

  it('imports a zip with its options and keeps the latest import of the view', async () => {
    const transfers = useTransfersStore()
    const zip = createZip([textFile('rst.no-such-annotation.conf.json', '{"a":1}'), textFile('export.json', '{}')])
    const file = new File([zip], 'configs.zip', { type: 'application/zip' })
    let query: URLSearchParams | undefined
    server.events.on('request:start', ({ request }) => {
      if (request.method === 'POST' && request.url.includes('/acme/billing/default/imports')) {
        query = new URL(request.url).searchParams
      }
    })

    const job = await transfers.startImport(scope, file, { dryRun: true, force: false })
    await settle(job)

    expect(query?.get('dryRun')).toBe('true')
    expect(query?.has('force')).toBe(false)
    expect(query?.get('fileName')).toBe('configs.zip')
    expect(job.phase).toBe('done')
    expect(job.state.Summary).toMatchObject({ Skipped: 1, IgnoredFiles: 1 })
    expect(job.state.Items).toEqual([
      expect.objectContaining({ Kind: 'Configuration', Outcome: 'Skipped', ErrorCode: 'AnnotationNotFound' }),
    ])
    expect(transfers.latestImport(scope)).toBe(job as ImportJob)
    expect(transfers.latestImport({ ...scope, view: 'staging' })).toBeUndefined()

    transfers.dismiss(job.id)
    expect(transfers.latestImport(scope)).toBeUndefined()
    expect(sessionStorage.getItem('2s-admin:latest-imports')).toBeNull()
  })

  it('keeps the latest export of each view', async () => {
    const transfers = useTransfersStore()

    const first = await transfers.startExport(scope, { format: 'Folder' })
    const second = await transfers.startExport(scope, { format: 'Git' })
    const other = await transfers.startExport({ ...scope, view: 'staging' }, { format: 'Folder' })

    expect(transfers.latestExport(scope)).toBe(second)
    expect(transfers.latestExport({ ...scope, view: 'staging' })).toBe(other)
    expect(first.phase).toBe('running')
    await settle(second)
  })

  /** A reload: a new store reads what the previous page left in sessionStorage. */
  async function reload(saved: string | null) {
    const { createPinia, setActivePinia } = await import('pinia')
    setActivePinia(createPinia())
    if (saved) {
      sessionStorage.setItem('2s-admin:latest-imports', saved)
    }
    const transfers = useTransfersStore()
    const finished: string[] = []
    transfers.onFinished((job) => finished.push(`${job.kind}:${job.phase}`))
    transfers.resume()
    return { transfers, finished }
  }

  const file = () => new File([createZip([textFile('rst.payments.json', '{}')])], 'view.zip')

  it('shows the latest import of a view again after a reload, without reporting it again', async () => {
    const first = useTransfersStore()
    const checked = await first.startImport(scope, file(), { dryRun: true, existing: 'Skip' })
    await settle(checked)
    const saved = sessionStorage.getItem('2s-admin:latest-imports')
    expect(JSON.parse(saved!)).toEqual([{ id: checked.id, scope, startedAt: checked.startedAt, finished: true }])

    const { transfers, finished } = await reload(saved)
    await vi.waitFor(() => expect(transfers.latestImport(scope)).toBeDefined())

    const restored = transfers.latestImport(scope)!
    expect(restored).toMatchObject({ id: checked.id, phase: 'done', startedAt: checked.startedAt })
    expect(restored.state).toMatchObject({ Status: 'Succeeded', IsDryRun: true, Existing: 'Skip' })
    // the file stayed with the previous page, so Import now is not offered
    expect(restored.file).toBeUndefined()
    expect(finished).toEqual([])
  })

  it('follows an import that ran during the reload and reports its end', async () => {
    const first = useTransfersStore()
    const running = await first.startImport(scope, file())
    const saved = sessionStorage.getItem('2s-admin:latest-imports')
    expect(JSON.parse(saved!)).toEqual([expect.objectContaining({ id: running.id, finished: false })])

    const { transfers, finished } = await reload(saved)
    await vi.waitFor(() => expect(transfers.latestImport(scope)).toBeDefined())
    const resumed = transfers.latestImport(scope)!
    await settle(resumed)

    expect(resumed.phase).toBe('done')
    expect(finished).toEqual(['import:done'])
    expect(JSON.parse(sessionStorage.getItem('2s-admin:latest-imports')!)).toEqual([
      expect.objectContaining({ id: running.id, finished: true }),
    ])
  })

  it('forgets a saved import that has expired, and one that a newer import replaced', async () => {
    const expired = JSON.stringify([{ id: 'gone', scope, startedAt: Date.now(), finished: true }])

    const { transfers } = await reload(expired)
    await vi.waitFor(() => expect(sessionStorage.getItem('2s-admin:latest-imports')).toBeNull())
    expect(transfers.latestImport(scope)).toBeUndefined()

    // older than the day the server keeps a job: not even asked for
    const old = JSON.stringify([{ id: 'old', scope, startedAt: Date.now() - 25 * 60 * 60_000, finished: true }])
    const later = await reload(old)
    const newer = await later.transfers.startImport(scope, file(), { dryRun: true })
    expect(JSON.parse(sessionStorage.getItem('2s-admin:latest-imports')!)).toEqual([
      expect.objectContaining({ id: newer.id }),
    ])
  })
})

describe('mock transfer options', () => {
  const { org, project, view } = scope

  /** The second read of a mock job ends it. */
  async function finishedImport(id: string) {
    await getViewImport(org, project, view, id)
    return await getViewImport(org, project, view, id)
  }

  /** The files of a folder export of the view. */
  async function exportedFiles() {
    const started = await startViewExport(org, project, view, { Format: 'Folder' })
    await getViewExport(org, project, view, started.Id!)
    const exported = await getViewExport(org, project, view, started.Id!)
    expect(exported.Include).toEqual(['Annotations', 'Schemas', 'Templates', 'Configurations'])
    return await readZip(
      new Uint8Array(await (await getViewExportArchive(org, project, view, started.Id!)).arrayBuffer()),
    )
  }

  it('keeps a stored configuration with existing=Skip and counts the kinds left out as excluded', async () => {
    const files = await exportedFiles()
    const manifest = JSON.parse(new TextDecoder().decode(files.find((file) => file.name === 'export.json')!.data)) as {
      Configurations: { AnnotationKey: string; File: string }[]
    }
    const { AnnotationKey: key, File: name } = manifest.Configurations[0]!
    const edited = createZip([textFile(name, '{"editedByTest":true}')])

    const skipped = await startViewImport(org, project, view, new Blob([edited]), { existing: 'Skip', dryRun: true })
    const excluded = await startViewImport(org, project, view, new Blob([edited]), {
      include: ['Annotations', 'Templates'],
    })

    expect(skipped).toMatchObject({
      Existing: 'Skip',
      Include: ['Annotations', 'Schemas', 'Templates', 'Configurations'],
    })
    expect((await finishedImport(skipped.Id!)).Items).toEqual([
      expect.objectContaining({
        Kind: 'Configuration',
        AnnotationKey: key,
        Outcome: 'Skipped',
        ErrorCode: 'DocumentExists',
      }),
    ])
    expect(excluded).toMatchObject({ Existing: 'Overwrite', Include: ['Annotations', 'Templates'] })
    expect((await finishedImport(excluded.Id!)).Summary).toMatchObject({ ExcludedFiles: 1, Updated: 0 })
  })

  it('exports no configuration when include leaves them out', async () => {
    const started = await startViewExport(org, project, view, { Include: ['templates' as ViewArchiveContent] })
    await getViewExport(org, project, view, started.Id!)

    const exported = await getViewExport(org, project, view, started.Id!)

    expect(exported.Include).toEqual(['Templates'])
    // the API leaves the null counts of the kinds left out out of its answer
    expect(exported).not.toHaveProperty('ConfigurationCount')
    expect(exported).not.toHaveProperty('AnnotationCount')
    expect(exported.TemplateCount).toBe(2)
  })

  it('keeps a finished import for the next page load, as the API keeps its jobs', async () => {
    const zip = new Blob([createZip([textFile('rst.invoicing.json', '{"Title":"Edited"}')])])
    const started = await startViewImport(org, project, view, zip, { dryRun: true, fileName: 'edited.zip' })
    const finished = await finishedImport(started.Id!)
    const kept = sessionStorage.getItem('2s-admin:mock-finished-imports')
    expect(JSON.parse(kept!)).toEqual({ [started.Id!]: { scope, state: finished } })

    // the next page load has a new mock module without the job in memory, but the same tab storage
    resetTransfers()
    sessionStorage.setItem('2s-admin:mock-finished-imports', kept!)

    expect(await getViewImport(org, project, view, started.Id!)).toEqual(finished)
    await expect(getViewImport(org, project, 'staging', started.Id!)).rejects.toMatchObject({ status: 404 })
  })

  it('refuses an unknown kind or existing mode like the API', async () => {
    const zip = new Blob([createZip([textFile('rst.a.json', '{}')])])

    const kind = await startViewImport(org, project, view, zip, { include: ['Secrets' as ViewArchiveContent] }).catch(
      (error: unknown) => error,
    )
    const existing = await startViewImport(org, project, view, zip, { existing: 'Merge' as ViewImportExisting }).catch(
      (error: unknown) => error,
    )
    const exportKind = await startViewExport(org, project, view, { Include: ['Secrets' as ViewArchiveContent] }).catch(
      (error: unknown) => error,
    )

    expect(hasErrorCode(kind, 'InvalidQuery')).toBe(true)
    expect((kind as StorytellerError).message).toContain("unknown kind 'Secrets'")
    expect(hasErrorCode(existing, 'InvalidQuery')).toBe(true)
    expect((exportKind as StorytellerError).status).toBe(400)
  })
})
