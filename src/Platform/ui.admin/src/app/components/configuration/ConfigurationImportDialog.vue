<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { Scope } from '@/stores/context'
import { useTransfersStore, type ImportJob, type ImportOptions } from '@/stores/transfers'
import { errorMessage } from '@/utils/errors'

/**
 * Imports a zip into the view (spec 4.3): every kind, recognized by the file names, replaces the stored document unless
 * it is equal. The dialog shows the progress and the result; closing it keeps the import going, and opening it again
 * shows the view's latest import.
 */
const props = defineProps<{ scope: Scope }>()
const open = defineModel<boolean>('open', { default: false })

const transfers = useTransfersStore()
const file = ref<File | null>(null)
const dryRun = ref(false)
const force = ref(false)
const jobId = ref<string>()
const starting = ref(false)
const error = ref<string>()
let release: (() => void) | undefined

const job = computed(() => transfers.find(jobId.value) as ImportJob | undefined)
const running = computed(() => job.value?.phase === 'running')
const result = computed(() => (job.value?.phase === 'done' ? job.value.state : undefined))
const progress = computed(() => {
  const state = job.value?.state
  return state?.TotalCount ? { done: state.ProcessedCount ?? 0, total: state.TotalCount } : undefined
})

function follow(id: string | undefined) {
  release?.()
  release = id && open.value ? transfers.showInDialog(id) : undefined
}

watch(open, (value) => {
  if (value) {
    // the latest import of the view, while it runs or until a new one starts
    jobId.value = transfers.latestImport(props.scope)?.id
    error.value = undefined
  }

  follow(jobId.value)
})

watch(jobId, (id) => follow(id))

watch(
  () => job.value?.phase,
  (phase) => {
    error.value = phase === 'failed' ? job.value?.error : undefined
  },
)

onBeforeUnmount(() => release?.())

async function start(options: ImportOptions, archive: File) {
  starting.value = true
  error.value = undefined

  try {
    jobId.value = (await transfers.startImport(props.scope, archive, options)).id
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    starting.value = false
  }
}

function importSelected() {
  if (file.value) {
    void start({ dryRun: dryRun.value, force: force.value }, file.value)
  }
}

/** The checked file again, for real, with the same options. */
function importNow() {
  if (job.value) {
    const { Include, Existing, IsForced } = job.value.state
    void start({ include: Include, existing: Existing, force: IsForced, dryRun: false }, job.value.file)
  }
}

function newImport() {
  if (jobId.value) {
    transfers.dismiss(jobId.value)
  }

  jobId.value = undefined
  file.value = null
  error.value = undefined
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="Import configurations"
    description="A zip of a view, for example an export. Files are recognized by their names: <key>.json is an annotation, <key>.conf.json a configuration, and templates and schemas have their own suffixes. Each document replaces the stored one unless it is equal; nothing is deleted."
    :ui="{ content: 'max-w-2xl', footer: 'justify-end' }"
  >
    <template #body>
      <div class="space-y-4">
        <template v-if="!job || job.phase === 'failed'">
          <UFileUpload
            v-model="file"
            accept=".zip,application/zip"
            label="Drop a zip here"
            description="Version files, export.json and hidden folders are ignored."
            icon="i-lucide-file-archive"
            class="min-h-36 w-full"
            :disabled="starting"
          />
          <div class="space-y-2">
            <UCheckbox
              v-model="dryRun"
              label="Only check, write nothing"
              description="Validates and compares every file."
            />
            <UCheckbox
              v-model="force"
              label="Store schemas and configurations that break each other"
              description="Without it, they fail and the rest is imported."
            />
          </div>
        </template>

        <div v-else-if="running" class="space-y-2" role="status" aria-live="polite">
          <UProgress v-if="progress" size="sm" :model-value="progress.done" :max="progress.total" />
          <UProgress v-else size="sm" animation="carousel" />
          <p class="text-sm text-muted">
            {{
              progress
                ? `${progress.done} of ${progress.total} files${job.state.IsDryRun ? ' checked' : ' imported'}…`
                : 'Waiting for the server…'
            }}
            You can close this dialog; the import continues.
          </p>
        </div>

        <template v-else-if="result">
          <p class="text-sm">
            <span class="font-medium">{{ result.FileName ?? 'The archive' }}</span>
            {{ result.IsDryRun ? 'was checked; nothing was written.' : 'was imported.' }}
          </p>
          <ConfigurationImportResult :result="result" :scope="scope" />
        </template>

        <UAlert
          v-if="error"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-x"
          title="The import failed"
          :description="error"
        />
      </div>
    </template>
    <template #footer>
      <template v-if="result">
        <UButton label="New import" color="neutral" variant="outline" @click="newImport" />
        <UButton
          v-if="result.IsDryRun"
          label="Import now"
          icon="i-lucide-upload"
          :loading="starting"
          @click="importNow"
        />
        <UButton v-else label="Close" @click="open = false" />
      </template>
      <template v-else>
        <UButton :label="running ? 'Close' : 'Cancel'" color="neutral" variant="ghost" @click="open = false" />
        <UButton
          v-if="!running"
          :label="dryRun ? 'Check' : 'Import'"
          icon="i-lucide-upload"
          :loading="starting"
          :disabled="!file"
          @click="importSelected"
        />
      </template>
    </template>
  </UModal>
</template>
