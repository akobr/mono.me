<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { dismissJobItem, invalidateInbox } from '@/queries/inbox'
import { invalidateImport } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import type { Scope } from '@/stores/context'
import { useTransfersStore, type ExportJob, type ImportJob, type TransferJob } from '@/stores/transfers'
import { exportCounts, exportImportPath, importCounts } from '@/utils/transfers'

// The ends of exports and imports (spec 4.5 of the Export and Import of a View), wherever the user is by then: a toast,
// and after an import that wrote documents, fresh queries of the kinds it wrote. An import that the Export & Import
// page shows gets no toast; any other links to the page, where its result is.
//
// The back end also leaves the end of every job in the inbox of its view (424-31), for the other tabs and the other
// Administrators. This tab has just shown that end, so it dismisses the job's item: the inbox must not report it a
// second time, nor light the mark of the menu for the person who saw it. The item is then under Done, with its
// Download or its result. What stays open is what nobody was shown.
const toast = useToast()
const queryClient = useQueryClient()
const catalog = useCatalogStore()
const transfers = useTransfersStore()

const inCatalog = (scope: Scope) =>
  catalog.scope?.org === scope.org && catalog.scope.project === scope.project && catalog.scope.view === scope.view

function reportExport(job: ExportJob) {
  const counts = exportCounts(job.state)
  toast.add(
    job.phase === 'done'
      ? {
          title: counts ? `Exported ${counts}` : 'Exported the view',
          description: job.state.FileName,
          color: 'success',
          icon: 'i-lucide-download',
        }
      : { title: 'The export failed', description: job.error, color: 'error', icon: 'i-lucide-circle-x' },
  )
}

function reportImport(job: ImportJob) {
  const show = [
    {
      label: 'Show the result',
      to: exportImportPath(job.scope),
      color: 'neutral' as const,
      variant: 'outline' as const,
    },
  ]
  toast.add(
    job.phase === 'done'
      ? {
          title: job.state.IsDryRun
            ? `Checked ${job.state.FileName ?? 'the archive'}`
            : `Imported ${job.state.FileName ?? 'the archive'}`,
          description: importCounts(job.state),
          color: (job.state.Summary?.Failed ?? 0) > 0 ? 'warning' : 'success',
          icon: 'i-lucide-upload',
          duration: 10_000,
          actions: show,
        }
      : {
          title: 'The import failed',
          description: job.error,
          color: 'error',
          icon: 'i-lucide-circle-x',
          actions: show,
        },
  )
}

async function refresh(job: ImportJob) {
  const written = await invalidateImport(queryClient, job.scope, job.state)
  if (!inCatalog(job.scope)) {
    return
  }

  // a re-create also changes the configurations, which the catalog loads with the annotations
  if (written.has('Annotations')) {
    await catalog.load(job.scope, { force: true })
  } else if (written.has('Configurations')) {
    await catalog.refreshConfigurations()
  }
}

/**
 * Whether the end the tab reported is the end of the job on the server. It is not when the tab gave up waiting or
 * reading, or could not download the archive of an export that succeeded: the job's item then tells what this tab
 * could not, so it stays open.
 */
const isReported = (job: TransferJob) =>
  job.phase === 'done' ? job.state.Status === 'Succeeded' : job.state.Status === 'Failed'

/** The inbox of the job's view, read again, after the item of a reported job was dismissed. Never a toast. */
function settleInbox(job: TransferJob) {
  void (isReported(job) ? dismissJobItem(queryClient, job.scope, job.id) : invalidateInbox(queryClient, job.scope))
}

const stop = transfers.onFinished((job: TransferJob) => {
  settleInbox(job)

  if (job.kind === 'import' && job.phase === 'done') {
    void refresh(job)
  }

  if (job.kind === 'export') {
    // the download starts by itself, so the toast confirms it
    reportExport(job)
  } else if (!transfers.watched.has(job.id)) {
    reportImport(job)
  }
})

onMounted(() => transfers.resume())
onBeforeUnmount(stop)
</script>

<template>
  <span hidden />
</template>
