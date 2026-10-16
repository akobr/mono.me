<script setup lang="ts">
import { computed, onBeforeUnmount, useTemplateRef, watch } from 'vue'
import { useContextStore } from '@/stores/context'
import { useTransfersStore } from '@/stores/transfers'
import ImportPanel from '@/components/transfer/ImportPanel.vue'

// Export & Import of the view (spec 4.1 of the Export and Import of a View): the Export and Import sections, side by
// side on desktop and stacked on phones, and below them the result of the view's latest import, also after a reload.
// Every role exports; importing needs a Contributor.
const context = useContextStore()
const transfers = useTransfersStore()
const canEdit = useCan('edit')
const importPanel = useTemplateRef<InstanceType<typeof ImportPanel>>('importPanel')

const scopeKey = computed(() =>
  context.scope ? `${context.scope.org}/${context.scope.project}/${context.scope.view}` : '',
)
const latest = computed(() => (context.scope ? transfers.latestImport(context.scope) : undefined))
const result = computed(() => (latest.value?.phase === 'done' ? latest.value.state : undefined))
// an import restored after a reload has no file to import again
const canImportNow = computed(() => canEdit.value && !!result.value?.IsDryRun && !!latest.value?.file)

// The page shows the end of its latest import, so the toaster does not report it; once the page is left, it does.
let release: (() => void) | undefined

watch(
  () => latest.value?.id,
  (id) => {
    release?.()
    release = id ? transfers.showInPage(id) : undefined
  },
  { immediate: true },
)

onBeforeUnmount(() => release?.())
</script>

<template>
  <UDashboardPanel id="export-import">
    <template #header>
      <AppNavbar title="Export & Import" />
    </template>
    <template #body>
      <div v-if="context.scope" class="mx-auto w-full max-w-6xl space-y-6">
        <div class="grid items-start gap-6 lg:grid-cols-2">
          <ExportPanel :key="`export:${scopeKey}`" :scope="context.scope" />
          <ImportPanel ref="importPanel" :key="`import:${scopeKey}`" :scope="context.scope" />
        </div>
        <ImportResult
          v-if="result"
          :result="result"
          :scope="context.scope"
          :can-import-now="canImportNow"
          :importing="importPanel?.starting"
          @import-now="importPanel?.importNow()"
        />
      </div>
    </template>
  </UDashboardPanel>
</template>
