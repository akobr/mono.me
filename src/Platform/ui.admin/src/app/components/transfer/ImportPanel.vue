<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { ViewArchiveContent, ViewImportExisting } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'
import { useTransfersStore, type ImportOptions } from '@/stores/transfers'
import { errorMessage } from '@/utils/errors'
import { archiveContents, contentItems } from '@/utils/transfers'

/**
 * The Import section of the Export & Import page (spec 4.3 of the Export and Import of a View): a zip, the kinds that
 * may be written, what happens to existing documents, and force. Check runs a dry run, Import a real one; the page
 * shows the result of the view's latest import below, with Import now after a check. Leaving the page does not stop
 * the import: TransferToaster reports its end.
 */
const props = defineProps<{ scope: Scope }>()

const transfers = useTransfersStore()
// the page mounts the panel for Administrators only (424-66), so its lock is a fallback
const canTransfer = useCan('transfer')
const file = ref<File | null>(null)
const include = ref<ViewArchiveContent[]>([...archiveContents])
const existing = ref<ViewImportExisting>('Overwrite')
const force = ref(false)
/** The run being started: a check (dry run) or an import. */
const starting = ref<'check' | 'import'>()
const error = ref<string>()

const existingItems = [
  {
    label: 'Overwrite',
    value: 'Overwrite',
    description:
      "The archive's content replaces them as a new version. Annotations are updated, and deleted ones are re-created with an empty configuration.",
  },
  { label: 'Skip', value: 'Skip', description: 'They stay as they are; only new documents are written.' },
]

const latest = computed(() => transfers.latestImport(props.scope))
const running = computed(() => latest.value?.phase === 'running')
const busy = computed(() => !!starting.value || running.value)
const progress = computed(() => {
  const state = latest.value?.state
  return state?.TotalCount ? { done: state.ProcessedCount ?? 0, total: state.TotalCount } : undefined
})

watch(
  () => latest.value?.phase,
  (phase) => {
    error.value = phase === 'failed' ? latest.value?.error : undefined
  },
  { immediate: true },
)

async function start(archive: File, options: ImportOptions) {
  starting.value = options.dryRun ? 'check' : 'import'
  error.value = undefined

  try {
    await transfers.startImport(props.scope, archive, options)
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    starting.value = undefined
  }
}

function run(dryRun: boolean) {
  if (file.value && include.value.length > 0 && !busy.value) {
    const kinds = archiveContents.filter((content) => include.value.includes(content))
    void start(file.value, { include: kinds, existing: existing.value, force: force.value, dryRun })
  }
}

/** The checked file again, for real, with the options of the check. */
function importNow() {
  const job = latest.value
  if (job?.file && job.state.IsDryRun && !busy.value) {
    const { Include, Existing, IsForced } = job.state
    void start(job.file, { include: Include, existing: Existing, force: IsForced, dryRun: false })
  }
}

defineExpose({ importNow, starting: computed(() => starting.value === 'import') })
</script>

<template>
  <UCard as="section" aria-labelledby="import-heading" :ui="{ body: 'space-y-4' }">
    <template #header>
      <h2 id="import-heading" class="font-semibold text-highlighted">Import</h2>
      <p class="mt-1 text-sm text-muted">
        The documents of a zip, for example an export, into {{ scope.view }}. Nothing is deleted.
      </p>
    </template>

    <UAlert
      v-if="!canTransfer"
      color="neutral"
      variant="subtle"
      icon="i-lucide-lock"
      title="Importing needs the Administrator role"
      description="Ask an Administrator or an Owner of the project to import an archive."
    />

    <template v-else>
      <UFileUpload
        v-model="file"
        accept=".zip,application/zip"
        label="Drop a zip here"
        description="Folders don't matter: files are recognized by their names. Version files, export.json and hidden folders are ignored."
        icon="i-lucide-file-archive"
        class="min-h-36 w-full"
        :disabled="busy"
      />

      <UFormField
        label="Kinds"
        description="What the import may write."
        :error="include.length === 0 ? 'Pick at least one kind.' : undefined"
      >
        <UCheckboxGroup v-model="include" :items="contentItems" :disabled="busy" />
      </UFormField>

      <UFormField label="Existing documents">
        <URadioGroup v-model="existing" :items="existingItems" variant="card" :disabled="busy" />
      </UFormField>

      <UCheckbox
        v-model="force"
        label="Store schemas and configurations that break each other"
        description="Without it, they fail and the rest is imported."
        :disabled="busy"
      />

      <div v-if="running" class="space-y-2" role="status" aria-live="polite">
        <UProgress v-if="progress" size="sm" :model-value="progress.done" :max="progress.total" />
        <UProgress v-else size="sm" animation="carousel" />
        <p class="text-sm text-muted">
          {{
            progress
              ? `${progress.done} of ${progress.total} files ${latest?.state.IsDryRun ? 'checked' : 'imported'}…`
              : 'Waiting for the server…'
          }}
          You can leave this page; the import continues.
        </p>
      </div>
      <UAlert
        v-if="error"
        color="error"
        variant="subtle"
        icon="i-lucide-circle-x"
        title="The import failed"
        :description="error"
      />

      <div class="flex flex-wrap justify-end gap-2">
        <UButton
          label="Check"
          icon="i-lucide-list-checks"
          color="neutral"
          variant="outline"
          :loading="starting === 'check'"
          :disabled="busy || !file || include.length === 0"
          @click="run(true)"
        />
        <UButton
          label="Import"
          icon="i-lucide-upload"
          :loading="starting === 'import'"
          :disabled="busy || !file || include.length === 0"
          @click="run(false)"
        />
      </div>
    </template>
  </UCard>
</template>
