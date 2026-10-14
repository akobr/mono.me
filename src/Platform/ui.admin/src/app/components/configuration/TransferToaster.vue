<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { invalidateAllConfigurations, invalidateConfiguration } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useTransfersStore, type ExportJob, type ImportJob, type TransferJob } from '@/stores/transfers'
import { importCounts, plural } from '@/utils/transfers'

// The ends of exports and imports (spec 4.2, 4.3), wherever the user is by then: a toast, and after an import that
// wrote documents, fresh configuration queries. An import that its dialog shows gets no toast.
const toast = useToast()
const queryClient = useQueryClient()
const catalog = useCatalogStore()
const transfers = useTransfersStore()

function reportExport(job: ExportJob) {
  toast.add(
    job.phase === 'done'
      ? {
          title: `Exported ${plural(job.state.ConfigurationCount ?? 0, 'configuration')}`,
          description: job.state.FileName,
          color: 'success',
          icon: 'i-lucide-download',
        }
      : { title: 'The export failed', description: job.error, color: 'error', icon: 'i-lucide-circle-x' },
  )
}

function reportImport(job: ImportJob) {
  toast.add(
    job.phase === 'done'
      ? {
          title: job.state.IsDryRun
            ? `Checked ${job.state.FileName ?? 'the import'}`
            : `Imported ${job.state.FileName ?? 'the archive'}`,
          description: importCounts(job.state),
          color: (job.state.Summary?.Failed ?? 0) > 0 ? 'warning' : 'success',
          icon: 'i-lucide-upload',
          duration: 10_000,
        }
      : { title: 'The import failed', description: job.error, color: 'error', icon: 'i-lucide-circle-x' },
  )
}

async function refresh(job: ImportJob) {
  const summary = job.state.Summary
  if (job.state.IsDryRun || !summary || (summary.Created ?? 0) + (summary.Updated ?? 0) === 0) {
    return
  }

  const written = (job.state.Items ?? []).filter((item) => item.Outcome === 'Created' || item.Outcome === 'Updated')
  await Promise.all(
    job.state.AreItemsTruncated
      ? [invalidateAllConfigurations(queryClient, job.scope)]
      : written.map((item) => invalidateConfiguration(queryClient, job.scope, item.AnnotationKey!)),
  )
  await catalog.refreshConfigurations()
}

const stop = transfers.onFinished((job: TransferJob) => {
  if (job.kind === 'import' && job.phase === 'done') {
    void refresh(job)
  }

  if (job.kind === 'export') {
    // the export dialog closes when the download starts, so the toast confirms it
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
