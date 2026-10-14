import { ref } from 'vue'
import { defineStore } from 'pinia'
import {
  getConfigurationExport,
  getConfigurationExportArchive,
  getConfigurationImport,
  isStorytellerError,
  startConfigurationExport,
  startConfigurationImport,
  type ConfigurationExport,
  type ConfigurationExportCreateFormat,
  type ConfigurationImport,
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
  state: ConfigurationExport
}

export interface ImportJob extends TransferJobBase {
  kind: 'import'
  state: ConfigurationImport
  /** The uploaded file, so a dry run can be imported for real. */
  file: File
}

export type TransferJob = ExportJob | ImportJob

export interface ImportOptions {
  dryRun: boolean
  force: boolean
}

interface PendingExport {
  id: string
  scope: Scope
  startedAt: number
}

/** Every second, then every two and three seconds. */
export const pollDelays = [1000, 1000, 1000, 2000, 2000, 3000]
export const maxWaitMs = 15 * 60_000
const maxReadFailures = 3
const pendingKey = '2s-admin:pending-exports'

const sameScope = (left: Scope, right: Scope) =>
  left.org === right.org && left.project === right.project && left.view === right.view

const wait = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * Exports and imports of configurations (spec 4.2, 4.3). The server runs them as jobs, so they are tracked here,
 * not by a page: leaving the Configurations page does not stop the download of a finished export, and pending
 * exports are resumed after a reload. Components show the progress; TransferToaster reports the ends.
 */
export const useTransfersStore = defineStore('transfers', () => {
  const jobs = ref<TransferJob[]>([])
  /** Jobs whose progress a dialog shows, so the toaster does not report them twice. */
  const watched = ref(new Set<string>())
  const listeners = new Set<(job: TransferJob) => void>()
  let resumed = false

  function find(id: string | undefined): TransferJob | undefined {
    return id ? jobs.value.find((job) => job.id === id) : undefined
  }

  /** The newest import of the view, running or finished. */
  function latestImport(scope: Scope): ImportJob | undefined {
    return jobs.value.findLast((job): job is ImportJob => job.kind === 'import' && sameScope(job.scope, scope))
  }

  function onFinished(listener: (job: TransferJob) => void): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  }

  function showInDialog(id: string): () => void {
    watched.value.add(id)
    return () => watched.value.delete(id)
  }

  async function startExport(scope: Scope, format: ConfigurationExportCreateFormat): Promise<ExportJob> {
    const state = await startConfigurationExport(scope.org, scope.project, scope.view, { Format: format })
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

  async function startImport(scope: Scope, file: File, options: ImportOptions): Promise<ImportJob> {
    const state = await startConfigurationImport(scope.org, scope.project, scope.view, file, {
      dryRun: options.dryRun || undefined,
      force: options.force || undefined,
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
    void follow(job)
    return job
  }

  /** Fetches the archive of a finished export and saves it under the export's name. */
  async function download(job: ExportJob): Promise<void> {
    const archive = await getConfigurationExportArchive(job.scope.org, job.scope.project, job.scope.view, job.id)
    saveBlob(archive, job.state.FileName ?? `configurations-${job.scope.view}.zip`)
  }

  /** Follows the exports that were still running before a reload. */
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
        state: { Id: pending.id, Status: 'Queued' } as ConfigurationExport,
      })
      void follow(job)
    }

    savePending()
  }

  function dismiss(id: string): void {
    jobs.value = jobs.value.filter((job) => job.id !== id)
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
          job.state = await getConfigurationExport(job.scope.org, job.scope.project, job.scope.view, job.id)
        } else {
          job.state = await getConfigurationImport(job.scope.org, job.scope.project, job.scope.view, job.id)
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

  return {
    jobs,
    watched,
    find,
    latestImport,
    onFinished,
    showInDialog,
    startExport,
    startImport,
    download,
    resume,
    dismiss,
  }
})
