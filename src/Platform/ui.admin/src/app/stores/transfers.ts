import { ref } from 'vue'
import { defineStore } from 'pinia'
import {
  getViewExport,
  getViewExportArchive,
  getViewImport,
  isStorytellerError,
  startViewExport,
  startViewImport,
  type ViewArchiveContent,
  type ViewExport,
  type ViewExportCreateFormat,
  type ViewImport,
  type ViewImportExisting,
} from '@42for.net/2splatform.sdk'
import { saveBlob } from '@/utils/download'
import { errorMessage } from '@/utils/errors'
import type { Scope } from './context'

/** running: the server works on it; downloading: the archive is fetched; done and failed are final. */
export type TransferPhase = 'running' | 'downloading' | 'done' | 'failed'

interface TransferJobBase {
  id: string
  scope: Scope
  phase: TransferPhase
  /** Why the job failed: the server's error, or why the client stopped waiting. */
  error?: string
  /** Date.now() of the start, for the waiting limit. */
  startedAt: number
}

export interface ExportJob extends TransferJobBase {
  kind: 'export'
  state: ViewExport
}

export interface ImportJob extends TransferJobBase {
  kind: 'import'
  state: ViewImport
  /** The uploaded file, so a dry run can be imported for real; an import restored after a reload has none. */
  file?: File
}

export type TransferJob = ExportJob | ImportJob

export interface ExportOptions {
  format: ViewExportCreateFormat
  /** The kinds to export; none or an empty list means all four, as in the API. */
  include?: readonly ViewArchiveContent[]
}

export interface ImportOptions {
  /** The kinds to import; none or an empty list means all four, as in the API. */
  include?: readonly ViewArchiveContent[]
  /** What happens to documents the view already has; the API's default is Overwrite. */
  existing?: ViewImportExisting
  /** Store schemas and configurations that break each other, with a warning. */
  force?: boolean
  /** Decide every outcome and warning without writing. */
  dryRun?: boolean
}

interface PendingExport {
  id: string
  scope: Scope
  startedAt: number
}

/** The latest import of a view, so the Export & Import page shows it again after a reload. */
interface SavedImport {
  id: string
  scope: Scope
  startedAt: number
  /** Whether it had ended before the reload; one that ends later is reported like any other. */
  finished: boolean
}

/** Every second, then every two and three seconds. */
export const pollDelays = [1000, 1000, 1000, 2000, 2000, 3000]
export const maxWaitMs = 15 * 60_000
/** The server keeps a job for 24 hours. */
const jobLifetimeMs = 24 * 60 * 60_000
const maxReadFailures = 3
const pendingKey = '2s-admin:pending-exports'
const importsKey = '2s-admin:latest-imports'

const sameScope = (left: Scope, right: Scope) =>
  left.org === right.org && left.project === right.project && left.view === right.view

const scopeKey = (scope: Scope) => `${scope.org}/${scope.project}/${scope.view}`

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/** The include of a request: the kinds in the given order, or none for all four. */
const includeOf = (include: readonly ViewArchiveContent[] | undefined) => (include?.length ? [...include] : undefined)

/**
 * Exports and imports of a view (export-import.md; spec 4.5 of the Export and Import of a View). The server runs them
 * as jobs, so they are tracked here, not by a page: leaving the page stops neither the download of a finished export
 * nor the following of an import. After a reload, pending exports are resumed, and the latest import of each view is
 * read again. The Export & Import page shows the progress; TransferToaster reports the ends.
 */
export const useTransfersStore = defineStore('transfers', () => {
  const jobs = ref<TransferJob[]>([])
  /** Jobs whose progress a page shows, so the toaster does not report them twice. */
  const watched = ref(new Set<string>())
  const listeners = new Set<(job: TransferJob) => void>()
  /** Saved imports that are being read again after a reload, by id. */
  const restoring = new Map<string, SavedImport>()
  /** Saved imports that could not be read for another reason than a 404: kept for a later reload. */
  const unreadable = new Map<string, SavedImport>()
  let resumed = false

  function find(id: string | undefined): TransferJob | undefined {
    return id ? jobs.value.find((job) => job.id === id) : undefined
  }

  /** The newest import of the view, running or finished. */
  function latestImport(scope: Scope): ImportJob | undefined {
    return jobs.value.findLast((job): job is ImportJob => job.kind === 'import' && sameScope(job.scope, scope))
  }

  /** The newest export of the view, running or finished. */
  function latestExport(scope: Scope): ExportJob | undefined {
    return jobs.value.findLast((job): job is ExportJob => job.kind === 'export' && sameScope(job.scope, scope))
  }

  function onFinished(listener: (job: TransferJob) => void): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  }

  /** Marks a job as shown by a page until the returned function is called. */
  function showInPage(id: string): () => void {
    watched.value.add(id)
    return () => watched.value.delete(id)
  }

  async function startExport(scope: Scope, options: ExportOptions): Promise<ExportJob> {
    const state = await startViewExport(scope.org, scope.project, scope.view, {
      Format: options.format,
      Include: includeOf(options.include),
    })
    const job = track<ExportJob>({
      kind: 'export',
      id: state.Id!,
      scope,
      state,
      phase: 'running',
      startedAt: Date.now(),
    })
    savePending()
    void follow(job)
    return job
  }

  /** Uploads a zip; the options left out get the API's defaults (all four kinds, Overwrite, no force, a real run). */
  async function startImport(scope: Scope, file: File, options: ImportOptions = {}): Promise<ImportJob> {
    const state = await startViewImport(scope.org, scope.project, scope.view, file, {
      include: includeOf(options.include),
      existing: options.existing,
      force: options.force || undefined,
      dryRun: options.dryRun || undefined,
      fileName: file.name,
    })
    const job = track<ImportJob>({
      kind: 'import',
      id: state.Id!,
      scope,
      state,
      file,
      phase: 'running',
      startedAt: Date.now(),
    })
    saveImports()
    void follow(job)
    return job
  }

  /** Fetches the archive of a finished export and saves it under the export's name. */
  async function download(job: ExportJob): Promise<void> {
    const archive = await getViewExportArchive(job.scope.org, job.scope.project, job.scope.view, job.id)
    saveBlob(archive, job.state.FileName ?? `export-${job.scope.view}.zip`)
  }

  /** Follows the exports that were still running before a reload, and reads the latest import of each view again. */
  function resume(): void {
    if (resumed) {
      return
    }

    resumed = true

    for (const pending of readPending()) {
      if (find(pending.id) || Date.now() - pending.startedAt > maxWaitMs) {
        continue
      }

      const job = track<ExportJob>({
        kind: 'export',
        id: pending.id,
        scope: pending.scope,
        startedAt: pending.startedAt,
        phase: 'running',
        state: { Id: pending.id, Status: 'Queued' } as ViewExport,
      })
      void follow(job)
    }

    savePending()

    for (const saved of readImports()) {
      if (!find(saved.id) && Date.now() - saved.startedAt < jobLifetimeMs) {
        restoring.set(saved.id, saved)
        void restoreImport(saved)
      }
    }
  }

  /**
   * Reads a saved import again: a finished one is shown as it ended, a running one is followed, and one that ended
   * during the reload is reported. An expired or unreadable job is forgotten, and so is one that a newer import of the
   * view has replaced meanwhile.
   */
  async function restoreImport(saved: SavedImport): Promise<void> {
    let state: ViewImport | undefined
    try {
      state = await getViewImport(saved.scope.org, saved.scope.project, saved.scope.view, saved.id)
    } catch (error) {
      if (!isStorytellerError(error) || error.status !== 404) {
        // a network or server failure: keep the record, a later reload reads it again
        restoring.delete(saved.id)
        unreadable.set(saved.id, saved)
        return
      }

      state = undefined
    }

    restoring.delete(saved.id)

    if (!state || latestImport(saved.scope)) {
      saveImports()
      return
    }

    const job = track<ImportJob>({
      kind: 'import',
      id: saved.id,
      scope: saved.scope,
      state,
      phase: 'running',
      startedAt: saved.startedAt,
    })

    if (state.Status !== 'Succeeded' && state.Status !== 'Failed') {
      void follow(job)
    } else if (saved.finished) {
      job.phase = state.Status === 'Succeeded' ? 'done' : 'failed'
      job.error = state.Status === 'Failed' ? (state.Error ?? 'The job failed.') : undefined
    } else if (state.Status === 'Succeeded') {
      finish(job, 'done')
    } else {
      finish(job, 'failed', state.Error ?? 'The job failed.')
    }

    saveImports()
  }

  function dismiss(id: string): void {
    jobs.value = jobs.value.filter((job) => job.id !== id)
    saveImports()
  }

  // The reactive proxy of the added job: every later change of it is seen by the components.
  function track<T extends TransferJob>(job: T): T {
    jobs.value.push(job)
    return jobs.value.at(-1) as T
  }

  async function follow(job: TransferJob): Promise<void> {
    let attempt = 0
    let failures = 0

    while (job.phase === 'running') {
      if (Date.now() - job.startedAt > maxWaitMs) {
        finish(job, 'failed', 'The job did not end within 15 minutes; it may still end on the server.')
        return
      }

      await wait(pollDelays[Math.min(attempt++, pollDelays.length - 1)]!)

      try {
        if (job.kind === 'export') {
          job.state = await getViewExport(job.scope.org, job.scope.project, job.scope.view, job.id)
        } else {
          job.state = await getViewImport(job.scope.org, job.scope.project, job.scope.view, job.id)
        }

        failures = 0
      } catch (error) {
        if ((isStorytellerError(error) && error.status === 404) || ++failures >= maxReadFailures) {
          finish(
            job,
            'failed',
            isStorytellerError(error) && error.status === 404 ? 'The job has expired.' : errorMessage(error),
          )
          return
        }

        continue
      }

      if (job.state.Status === 'Failed') {
        finish(job, 'failed', job.state.Error ?? 'The job failed.')
      } else if (job.state.Status === 'Succeeded') {
        if (job.kind === 'export') {
          job.phase = 'downloading'

          try {
            await download(job)
          } catch (error) {
            finish(job, 'failed', `The archive could not be downloaded: ${errorMessage(error)}`)
            return
          }
        }

        finish(job, 'done')
      }
    }
  }

  function finish(job: TransferJob, phase: 'done' | 'failed', error?: string): void {
    job.phase = phase
    job.error = error

    if (job.kind === 'export') {
      savePending()
    } else {
      saveImports()
    }

    for (const listener of listeners) {
      listener(job)
    }
  }

  function savePending(): void {
    const pending: PendingExport[] = jobs.value
      .filter((job) => job.kind === 'export' && (job.phase === 'running' || job.phase === 'downloading'))
      .map((job) => ({ id: job.id, scope: job.scope, startedAt: job.startedAt }))

    try {
      if (pending.length) {
        sessionStorage.setItem(pendingKey, JSON.stringify(pending))
      } else {
        sessionStorage.removeItem(pendingKey)
      }
    } catch {
      // storage can be disabled; a reload then forgets the export, the server still finishes it
    }
  }

  function readPending(): PendingExport[] {
    try {
      const value = JSON.parse(sessionStorage.getItem(pendingKey) ?? '[]') as unknown
      return Array.isArray(value) ? (value as PendingExport[]) : []
    } catch {
      return []
    }
  }

  /** The latest import of every view: the tracked ones, and the saved ones that are still being read again. */
  function saveImports(): void {
    const latest = new Map<string, SavedImport>()

    for (const saved of [...unreadable.values(), ...restoring.values()]) {
      latest.set(scopeKey(saved.scope), saved)
    }

    for (const job of jobs.value) {
      if (job.kind === 'import') {
        latest.set(scopeKey(job.scope), {
          id: job.id,
          scope: job.scope,
          startedAt: job.startedAt,
          finished: job.phase !== 'running',
        })
      }
    }

    const saved = [...latest.values()]

    try {
      if (saved.length) {
        sessionStorage.setItem(importsKey, JSON.stringify(saved))
      } else {
        sessionStorage.removeItem(importsKey)
      }
    } catch {
      // storage can be disabled; a reload then forgets the import, which the server keeps for 24 hours
    }
  }

  function readImports(): SavedImport[] {
    try {
      const value = JSON.parse(sessionStorage.getItem(importsKey) ?? '[]') as unknown
      return Array.isArray(value) ? (value as SavedImport[]) : []
    } catch {
      return []
    }
  }

  return {
    jobs,
    watched,
    find,
    latestImport,
    latestExport,
    onFinished,
    showInPage,
    startExport,
    startImport,
    download,
    resume,
    dismiss,
  }
})
