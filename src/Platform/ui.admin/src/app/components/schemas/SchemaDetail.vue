<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMediaQuery } from '@vueuse/core'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { isConflict, isNotFound, type SchemaValidationErrorDetail } from '@42for.net/2splatform.sdk'
import { invalidateSchemas } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import { cloneJson } from '@/utils/clone'
import { errorMessage } from '@/utils/errors'
import { schemaApi, starterSchema, type SchemaTarget } from '@/utils/schema-targets'
import { validateSchemaDocument } from '@/utils/schema-validation'
import type { VersionSource } from '@/components/versions/version-source'

/**
 * The schema editor (spec section 14): the document on the left, validated against the JSON Schema meta-schema,
 * and the live form preview on the right (tabs on phones). Saving replaces the whole document (PUT); a 409 lists
 * the configurations the change would break, with Save anyway.
 */
const props = defineProps<{ target: SchemaTarget }>()

const route = useRoute()
const router = useRouter()
const context = useContextStore()
const catalog = useCatalogStore()
const preferences = usePreferencesStore()
const queryClient = useQueryClient()
const confirm = useConfirm()
const toast = useToast()
const canEdit = useCan('edit')
const wide = useMediaQuery('(min-width: 1280px)')

const api = computed(() => schemaApi(context.scope!, props.target))

const schema = useQuery({
  queryKey: computed(() => [...api.value.queryKey, 'document']),
  queryFn: ({ signal }) => api.value.get(signal),
  meta: { silent: true },
})

const missing = computed(() => schema.isError.value && isNotFound(schema.error.value))
const original = ref<unknown>()
const draft = ref<unknown>()
const format = ref<DocumentFormat>(preferences.state.editorFormat)
const textValid = ref(true)
const saving = ref(false)
const nonCompliant = ref<SchemaValidationErrorDetail[]>([])
const confirmDelete = ref(false)
const pane = ref<'edit' | 'preview'>('edit')

watch(
  () => schema.data.value?.Content,
  (content) => {
    if (content !== undefined && JSON.stringify(draft.value) === JSON.stringify(original.value)) {
      original.value = cloneJson(content)
      draft.value = cloneJson(content)
    }
  },
  { immediate: true },
)
watch(missing, (value) => {
  if (value && draft.value === undefined) {
    draft.value = cloneJson(starterSchema)
  }
})

const dirty = computed(() => JSON.stringify(draft.value ?? null) !== JSON.stringify(original.value ?? null))
useUnsavedChanges(dirty)

const metaIssues = (value: unknown) =>
  validateSchemaDocument(value).map((issue) => ({ path: issue.pointer || '/', message: issue.message }))

const tab = computed({
  get: () => (route.query.tab === 'versions' ? 'versions' : 'edit'),
  set: (value: string) =>
    void router.replace({
      query: { ...route.query, tab: value === 'edit' ? undefined : value, v: undefined, from: undefined },
    }),
})

const affected = computed(() => {
  void catalog.revision
  const keys = catalog.configurations ? [...catalog.configurations.keys()] : []
  return keys.filter((key) => api.value.appliesTo(key, catalog.ancestorsOf(key))).length
})

const versionSource = computed<VersionSource>(() => ({ ...api.value.versions, current: schema.data.value?.Version }))

async function save(force = false) {
  if (!context.scope || !draft.value || saving.value) {
    return
  }
  saving.value = true
  nonCompliant.value = []
  try {
    const saved = await api.value.set(draft.value as Record<string, unknown>, force)
    original.value = cloneJson(draft.value)
    await invalidateSchemas(queryClient, context.scope)
    toast.add({ title: `Schema saved (v${saved.Version ?? '?'})`, color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    if (isConflict(error) && error.schemaErrors?.length) {
      nonCompliant.value = error.schemaErrors
    } else {
      toast.add({ title: 'The schema was not saved', description: errorMessage(error), color: 'error' })
    }
  } finally {
    saving.value = false
  }
}

async function forceSave() {
  const confirmed = await confirm({
    title: 'Save although configurations break?',
    description: `${nonCompliant.value.length} configuration(s) will no longer comply with the schema. They keep working, but their next save without force fails until they are fixed.`,
    confirmLabel: 'Save anyway',
    color: 'error',
  })
  if (confirmed) {
    await save(true)
  }
}

async function remove() {
  if (!context.scope) {
    return
  }
  try {
    await api.value.remove()
    confirmDelete.value = false
    original.value = undefined
    draft.value = undefined
    await invalidateSchemas(queryClient, context.scope)
    toast.add({ title: 'Schema deleted', color: 'success', icon: 'i-lucide-trash-2' })
  } catch (error) {
    toast.add({ title: 'Delete failed', description: errorMessage(error), color: 'error' })
  }
}

defineShortcuts({
  meta_s: { usingInput: true, handler: () => canEdit.value && dirty.value && void save() },
})
</script>

<template>
  <div class="flex min-h-full flex-col">
    <div class="space-y-1 px-4 pt-3">
      <p class="text-sm text-muted">{{ api.description }}</p>
      <p v-if="catalog.configurations" class="text-xs text-dimmed">
        Applies to {{ affected }} configuration{{ affected === 1 ? '' : 's' }} of this view.
      </p>
      <UTabs
        v-model="tab"
        :items="[
          { label: 'Edit', value: 'edit', icon: 'i-lucide-pencil' },
          { label: 'Versions', value: 'versions', icon: 'i-lucide-history' },
        ]"
        :content="false"
        size="xs"
        variant="link"
      />
    </div>

    <VersionsView v-if="tab === 'versions' && !missing" :source="versionSource" />
    <EmptyState v-else-if="tab === 'versions'" icon="i-lucide-history" title="No schema, no history" />

    <template v-else>
      <LoadingState v-if="schema.isPending.value" />
      <ErrorState v-else-if="schema.isError.value && !missing" :error="schema.error.value" @retry="schema.refetch()" />
      <template v-else>
        <div class="flex-1 space-y-3 p-4">
          <UAlert
            v-if="missing && !dirty"
            color="neutral"
            variant="subtle"
            icon="i-lucide-info"
            title="This schema does not exist yet"
            description="Edit the starter document and save to create it."
          />
          <UAlert
            v-if="nonCompliant.length"
            color="error"
            variant="subtle"
            icon="i-lucide-shield-alert"
            title="Existing configurations would not comply"
            :close="true"
            @update:open="(open) => !open && (nonCompliant = [])"
          >
            <template #description>
              <NonCompliantTable :details="nonCompliant" class="mt-2" />
              <UButton
                v-if="canEdit"
                class="mt-2"
                label="Save anyway"
                color="error"
                variant="outline"
                size="xs"
                :loading="saving"
                @click="forceSave"
              />
            </template>
          </UAlert>

          <UTabs
            v-if="!wide"
            v-model="pane"
            :items="[
              { label: 'Schema', value: 'edit' },
              { label: 'Preview', value: 'preview' },
            ]"
            :content="false"
            size="xs"
          />
          <div :class="wide ? 'grid grid-cols-2 gap-4' : ''">
            <DocumentEditor
              v-show="wide || pane === 'edit'"
              :model-value="draft ?? {}"
              :format="format"
              :readonly="!canEdit"
              :validate="metaIssues"
              aria-label="Schema document"
              @update:model-value="(value) => (draft = value)"
              @update:format="(value) => (format = value)"
              @update:valid="(valid) => (textValid = valid)"
              @save="canEdit && dirty && save()"
            />
            <SchemaFormPreview v-show="wide || pane === 'preview'" :schema="draft" />
          </div>
        </div>

        <div
          v-if="canEdit"
          class="sticky bottom-0 flex items-center justify-end gap-2 border-t border-default bg-default/95 px-4 py-3 backdrop-blur"
        >
          <UButton
            v-if="!missing"
            class="me-auto"
            label="Delete"
            icon="i-lucide-trash-2"
            color="neutral"
            variant="ghost"
            size="sm"
            @click="confirmDelete = true"
          />
          <UButton
            label="Revert"
            color="neutral"
            variant="ghost"
            :disabled="!dirty || saving"
            @click="draft = cloneJson(original ?? starterSchema)"
          />
          <UButton
            :label="missing ? 'Create' : 'Save'"
            :loading="saving"
            :disabled="(!dirty && !missing) || !textValid"
            @click="save()"
          />
        </div>
      </template>
    </template>

    <ConfirmDialog
      v-model:open="confirmDelete"
      :title="`Delete the ${api.title}?`"
      description="Configurations are no longer validated by it, and the form disappears where it was the only schema."
      confirm-text="delete"
      @confirm="remove"
    />
  </div>
</template>
