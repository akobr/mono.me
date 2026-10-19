<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { exportKeyValues, serializeDotEnv, toFlatJson, type KeyValueDocument } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'

/**
 * Export of the view's pairs (spec 29.5) as the key/value document, .env lines or a flat JSON object. Secret values
 * only on request and for the secrets capability; the download goes through a Blob URL, never through the query cache.
 */
const context = useContextStore()
const toast = useToast()
const canSecrets = useCan('secrets')
const includeSecrets = ref(false)
const busy = ref(false)
// A request for one view: the switcher keeps Values store, and this menu, through a view or project switch (424-68).
watch([() => context.projectKey, () => context.view], () => (includeSecrets.value = false))

async function download(format: 'json' | 'env' | 'flat') {
  if (!context.scope || busy.value) {
    return
  }
  busy.value = true
  try {
    const { org, project, view } = context.scope
    // the request of this download, so its warning matches the file even if the opt-in changes meanwhile
    const withSecrets = includeSecrets.value && canSecrets.value
    const document: KeyValueDocument = await exportKeyValues(org, project, view, { IncludeSecrets: withSecrets })
    const items = document.Items ?? []
    const withoutValue = items.filter((item) => item.Value === undefined || item.Value === null).length
    let text: string
    let name: string
    let type: string

    if (format === 'json') {
      text = JSON.stringify(document, null, 2)
      name = `${org}-${project}-${view}.key-values.json`
      type = 'application/json'
    } else if (format === 'env') {
      text = serializeDotEnv(items.filter((item) => item.Value != null).map((item) => ({ key: item.Key ?? '', value: item.Value ?? '' })))
      name = `${org}-${project}-${view}.env`
      type = 'text/plain'
    } else {
      text = JSON.stringify(toFlatJson(document), null, 2)
      name = `${org}-${project}-${view}.flat.json`
      type = 'application/json'
    }

    const url = URL.createObjectURL(new Blob([text], { type }))
    const link = window.document.createElement('a')
    link.href = url
    link.download = name
    link.click()
    // revoked after the click has been handled; an immediate revoke can cancel the download in some browsers
    setTimeout(() => URL.revokeObjectURL(url), 1000)

    if (format !== 'json' && withoutValue > 0) {
      toast.add({
        title: `${withoutValue} secret(s) left out`,
        description: 'The .env and flat formats carry values only; export the key/value document, or include the secret values.',
        color: 'warning',
        icon: 'i-lucide-triangle-alert',
      })
    } else if (withSecrets) {
      toast.add({ title: 'The file contains secret values', description: 'Store it where it belongs, then delete it.', color: 'warning', icon: 'i-lucide-key-round' })
    }
  } catch (error) {
    toast.add({ title: 'The export failed', description: errorMessage(error), color: 'error' })
  } finally {
    busy.value = false
  }
}

const items = computed(() => [
  [
    { label: 'Key/value document (JSON)', icon: 'i-lucide-file-json', onSelect: () => download('json') },
    { label: '.env file', icon: 'i-lucide-file-terminal', onSelect: () => download('env') },
    { label: 'Flat JSON object', icon: 'i-lucide-braces', onSelect: () => download('flat') },
  ],
  ...(canSecrets.value
    ? [[{ label: 'Include secret values', type: 'checkbox' as const, checked: includeSecrets.value, onUpdateChecked: (checked: boolean) => (includeSecrets.value = checked), onSelect: (event: Event) => event.preventDefault() }]]
    : []),
])
</script>

<template>
  <UDropdownMenu :items="items" :content="{ align: 'end' }">
    <UButton label="Export" aria-label="Export" icon="i-lucide-download" color="neutral" variant="outline" :loading="busy" :ui="{ label: 'hidden sm:inline' }" />
  </UDropdownMenu>
</template>
