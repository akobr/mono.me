<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { getViewImport, isNotFound, type ViewImport, type ViewInboxItem } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'

// The result of an import that an item of the inbox leads to (424-31): the report of the item's job, read by its id
// when the slideover opens. The slideover is wide, because the report is a table from 768 px up. It offers no
// "Import now": the file of a check stays in the tab that uploaded it. A job that has expired closes the slideover
// with a toast; any other failure is shown in place, with Retry.
const props = defineProps<{ scope: Scope; item?: ViewInboxItem }>()
const emit = defineEmits<{ close: [] }>()

const toast = useToast()

const open = computed({
  get: () => !!props.item,
  set: (value: boolean) => !value && emit('close'),
})

const result = ref<ViewImport>()
const failure = ref<unknown>()
const loading = ref(false)
// A report belongs to the item it was asked for: one that arrives after another item was opened is dropped.
let reading = 0

async function read() {
  const id = props.item?.Job?.Id
  const started = ++reading
  result.value = undefined
  failure.value = undefined
  loading.value = !!id
  if (!id) {
    return
  }

  try {
    const state = await getViewImport(props.scope.org, props.scope.project, props.scope.view, id)
    if (started === reading) {
      result.value = state
    }
  } catch (error) {
    if (started !== reading) {
      return
    }
    if (isNotFound(error)) {
      toast.add({
        title: 'The report has expired',
        description: 'The report of an import is kept for a limited time.',
        color: 'warning',
        icon: 'i-lucide-clock',
      })
      emit('close')
    } else {
      failure.value = error
    }
  } finally {
    if (started === reading) {
      loading.value = false
    }
  }
}

watch(() => props.item?.Job?.Id, read, { immediate: true })
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="item?.Title ?? 'Result of the import'"
    description="The report of the import, as its job left it."
    :ui="{ content: 'max-w-5xl' }"
  >
    <template #body>
      <LoadingState v-if="loading" label="Loading the report…" />
      <ErrorState v-else-if="failure" :error="failure" @retry="read" />
      <ImportResult v-else-if="result" :result="result" :scope="scope" title="Report" />
    </template>
  </USlideover>
</template>
