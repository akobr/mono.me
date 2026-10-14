<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ConfigurationExportCreateFormat } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'
import { useTransfersStore, type ExportJob } from '@/stores/transfers'
import { errorMessage } from '@/utils/errors'

/**
 * Starts an export of the view's configurations (spec 4.2). The server builds the zip in the background; when it is
 * ready, the store downloads it and the dialog closes. Closing the dialog earlier keeps the export going.
 */
const props = defineProps<{ scope: Scope }>()
const open = defineModel<boolean>('open', { default: false })

const transfers = useTransfersStore()
const format = ref<ConfigurationExportCreateFormat>('Folder')
const jobId = ref<string>()
const starting = ref(false)
const error = ref<string>()

const formats = [
  {
    label: 'Folder',
    value: 'Folder',
    description: 'The current documents as <key>.json and earlier versions as <key>.<version>.json, with export.json.',
  },
  {
    label: 'Git repository',
    value: 'Git',
    description: 'A repository with the current documents checked out; every version is a commit by its author.',
  },
]

const job = computed(() => transfers.find(jobId.value) as ExportJob | undefined)
const running = computed(() => job.value?.phase === 'running' || job.value?.phase === 'downloading')
const status = computed(() => {
  if (job.value?.phase === 'downloading') {
    return 'Downloading the archive…'
  }

  return job.value?.state.Status === 'Running' ? 'Building the archive…' : 'Waiting for the server…'
})

watch(open, (value) => {
  if (value && !running.value) {
    jobId.value = undefined
    error.value = undefined
  }
})

watch(
  () => job.value?.phase,
  (phase) => {
    if (phase === 'done') {
      open.value = false
    } else if (phase === 'failed') {
      error.value = job.value?.error
    }
  },
)

async function start() {
  starting.value = true
  error.value = undefined

  try {
    jobId.value = (await transfers.startExport(props.scope, format.value)).id
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    starting.value = false
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="Export configurations"
    :description="`The own documents of every configuration in ${scope.view}, as a zip. Templates, schemas and secrets are not included.`"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div class="space-y-4">
        <URadioGroup
          v-model="format"
          :items="formats"
          variant="card"
          :disabled="running || starting"
          aria-label="Format of the archive"
        />
        <div v-if="running" class="space-y-2" role="status" aria-live="polite">
          <UProgress size="sm" animation="carousel" />
          <p class="text-sm text-muted">
            {{ status }} You can close this dialog; the download starts when it is ready.
          </p>
        </div>
        <UAlert
          v-if="error"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-x"
          title="The export failed"
          :description="error"
        />
      </div>
    </template>
    <template #footer>
      <UButton :label="running ? 'Close' : 'Cancel'" color="neutral" variant="ghost" @click="open = false" />
      <UButton
        label="Export"
        icon="i-lucide-download"
        :loading="starting || running"
        :disabled="running"
        @click="start"
      />
    </template>
  </UModal>
</template>
