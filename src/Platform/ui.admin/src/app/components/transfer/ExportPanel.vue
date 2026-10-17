<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ViewArchiveContent, ViewExportCreateFormat } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'
import { useTransfersStore } from '@/stores/transfers'
import { errorMessage } from '@/utils/errors'
import { archiveContents, contentItems, neverExported } from '@/utils/transfers'

/**
 * The Export section of the Export & Import page (spec 4.2 of the Export and Import of a View): the kinds, all four
 * by default, and the format. The server builds the zip in the background; the store downloads it when it is ready,
 * also after the user has left the page, and TransferToaster confirms it.
 */
const props = defineProps<{ scope: Scope }>()

const transfers = useTransfersStore()
const include = ref<ViewArchiveContent[]>([...archiveContents])
const format = ref<ViewExportCreateFormat>('Folder')
const starting = ref(false)
const error = ref<string>()

const formats = [
  {
    label: 'Folder of JSON files',
    value: 'Folder',
    description: 'One folder per kind with the current documents, earlier versions as version files, and export.json.',
  },
  {
    label: 'Git repository',
    value: 'Git',
    description:
      'The current documents checked out; every version of a schema, template or configuration is a commit by its author.',
  },
]

// the export of the view that runs or ran last: also one started before the user left the page, and one that the store
// follows again after a reload, which may be tracked only after this section is set up
const job = computed(() => transfers.latestExport(props.scope))
const running = computed(() => job.value?.phase === 'running' || job.value?.phase === 'downloading')
const busy = computed(() => starting.value || running.value)
const status = computed(() => {
  if (job.value?.phase === 'downloading') {
    return 'Downloading the archive…'
  }

  return job.value?.state.Status === 'Running' ? 'Building the archive…' : 'Waiting for the server…'
})

watch(
  () => job.value?.phase,
  (phase) => {
    error.value = phase === 'failed' ? job.value?.error : undefined
  },
  { immediate: true },
)

async function start() {
  if (busy.value || include.value.length === 0) {
    return
  }

  starting.value = true
  error.value = undefined

  try {
    // the kinds in import order, as the API lists them
    const kinds = archiveContents.filter((content) => include.value.includes(content))
    // the new export becomes the latest of the view, so the section shows it
    await transfers.startExport(props.scope, { format: format.value, include: kinds })
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    starting.value = false
  }
}
</script>

<template>
  <UCard as="section" aria-labelledby="export-heading" :ui="{ body: 'space-y-4' }">
    <template #header>
      <h2 id="export-heading" class="font-semibold text-highlighted">Export</h2>
      <p class="mt-1 text-sm text-muted">The documents of {{ scope.view }} as a zip.</p>
    </template>

    <UFormField label="Kinds" :error="include.length === 0 ? 'Pick at least one kind.' : undefined">
      <UCheckboxGroup v-model="include" :items="contentItems" :disabled="busy" />
    </UFormField>

    <UFormField label="Format">
      <URadioGroup v-model="format" :items="formats" variant="card" :disabled="busy" />
    </UFormField>

    <p class="text-xs text-muted">{{ neverExported }}</p>

    <div v-if="running" class="space-y-2" role="status" aria-live="polite">
      <UProgress size="sm" animation="carousel" />
      <p class="text-sm text-muted">{{ status }} The download starts when it is ready; you can leave this page.</p>
    </div>
    <p v-else-if="job?.phase === 'done'" class="flex items-center gap-1.5 text-sm text-muted" role="status">
      <UIcon name="i-lucide-circle-check" class="size-4 shrink-0 text-success" />
      <span class="min-w-0 break-all">{{ job.state.FileName ?? 'The archive' }} was downloaded.</span>
    </p>
    <UAlert
      v-if="error"
      color="error"
      variant="subtle"
      icon="i-lucide-circle-x"
      title="The export failed"
      :description="error"
    />

    <div class="flex justify-end">
      <UButton
        label="Export"
        icon="i-lucide-download"
        :loading="busy"
        :disabled="busy || include.length === 0"
        @click="start"
      />
    </div>
  </UCard>
</template>
