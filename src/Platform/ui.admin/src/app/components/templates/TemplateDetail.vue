<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  deleteTemplate,
  getTemplate,
  getTemplateVersion,
  getTemplateVersionDiffCustom,
  getTemplateVersions,
  hasErrorCode,
  isNotFound,
  isPreconditionFailed,
  patchTemplate,
  setTemplate,
  type DiffResult,
} from '@42for.net/2splatform.sdk'
import { invalidateAllConfigurations, invalidateTemplate } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import { metaOf, type AnnotationTypeCode } from '@/utils/annotation-key'
import { cloneJson } from '@/utils/clone'
import { errorMessage } from '@/utils/errors'
import { buildPatch, changeCount } from '@/utils/json-patch'
import { bindingDecorations } from '@/components/editor/setup/bindings'
import type { VersionSource } from '@/components/versions/version-source'

/**
 * A configuration template (spec section 14): one per annotation type and view, merged between the ancestors and
 * the own content. Edits are saved with PatchTemplate and `test` guards, like configurations.
 */
const props = defineProps<{ type: AnnotationTypeCode }>()

const route = useRoute()
const router = useRouter()
const context = useContextStore()
const catalog = useCatalogStore()
const preferences = usePreferencesStore()
const queryClient = useQueryClient()
const toast = useToast()
const canEdit = useCan('edit')

const meta = computed(() => metaOf(props.type)!)
const path = computed(() => `/v1/${context.org}/${context.project}/${context.view}/template/${props.type}`)
const queryKey = computed(() => path.value.split('/').filter(Boolean))

const template = useQuery({
  queryKey: computed(() => [...queryKey.value, 'document']),
  queryFn: ({ signal }) => getTemplate(context.org!, context.project!, context.view!, props.type, { signal }),
  meta: { silent: true },
})

const missing = computed(() => template.isError.value && isNotFound(template.error.value))
const original = ref<unknown>()
const draft = ref<unknown>()
const format = ref<DocumentFormat>(preferences.state.editorFormat)
const textValid = ref(true)
const saving = ref(false)
const conflictOpen = ref(false)
const theirs = ref<unknown>()
const confirmDelete = ref(false)

watch(
  () => template.data.value?.Content,
  (content) => {
    if (content !== undefined && JSON.stringify(draft.value) === JSON.stringify(original.value)) {
      original.value = cloneJson(content)
      draft.value = cloneJson(content)
    }
  },
  { immediate: true },
)

const dirty = computed(() => JSON.stringify(draft.value ?? null) !== JSON.stringify(original.value ?? null))
const patch = computed(() => (dirty.value && !missing.value ? buildPatch(original.value, draft.value) : []))
useUnsavedChanges(dirty)

const affected = computed(() => {
  void catalog.revision
  return catalog.configurations
    ? [...catalog.configurations.keys()].filter((key) => metaOf(key.slice(0, 3))?.code === props.type).length
    : undefined
})

const tab = computed({
  get: () => (route.query.tab === 'versions' ? 'versions' : 'edit'),
  set: (value: string) =>
    void router.replace({
      query: { ...route.query, tab: value === 'edit' ? undefined : value, v: undefined, from: undefined },
    }),
})

const versionSource = computed<VersionSource>(() => {
  const [org, project, view, type] = [context.org!, context.project!, context.view!, props.type]
  return {
    queryKey: queryKey.value,
    list: (signal) => getTemplateVersions(org, project, view, type, { signal }),
    content: async (version, signal) =>
      (await getTemplateVersion(org, project, view, type, version, { signal })).Content,
    diff: async (version, from, signal) =>
      (await getTemplateVersionDiffCustom(org, project, view, type, version, from, undefined, {
        signal,
      })) as DiffResult,
    current: template.data.value?.Version,
  }
})

async function afterSave() {
  await invalidateTemplate(queryClient, context.scope!, props.type)
  // Effective configurations of this type include the template.
  await invalidateAllConfigurations(queryClient, context.scope!)
}

async function save() {
  if (!context.scope) {
    return
  }
  const { org, project, view } = context.scope
  saving.value = true
  try {
    const saved = missing.value
      ? await setTemplate(org, project, view, props.type, (draft.value ?? {}) as Record<string, unknown>)
      : await patchTemplate(org, project, view, props.type, patch.value)
    original.value = cloneJson(draft.value)
    await afterSave()
    toast.add({ title: `Template saved (v${saved.Version ?? '?'})`, color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    if (isPreconditionFailed(error) || hasErrorCode(error, 'PatchTestFailed')) {
      conflictOpen.value = true
      theirs.value = (await template.refetch()).data?.Content
    } else {
      toast.add({ title: 'The template was not saved', description: errorMessage(error), color: 'error' })
    }
  } finally {
    saving.value = false
  }
}

async function overwrite() {
  original.value = cloneJson(theirs.value)
  conflictOpen.value = false
  await save()
}

async function remove() {
  if (!context.scope) {
    return
  }
  const { org, project, view } = context.scope
  try {
    await deleteTemplate(org, project, view, props.type)
    confirmDelete.value = false
    original.value = undefined
    draft.value = undefined
    await afterSave()
    toast.add({ title: 'Template deleted', color: 'success', icon: 'i-lucide-trash-2' })
  } catch (error) {
    toast.add({ title: 'Delete failed', description: errorMessage(error), color: 'error' })
  }
}

function start() {
  draft.value = {}
}

defineShortcuts({
  meta_s: { usingInput: true, handler: () => canEdit.value && dirty.value && void save() },
})
</script>

<template>
  <div class="flex min-h-full flex-col">
    <div class="space-y-1 px-4 pt-3">
      <p class="text-sm text-muted">
        Default configuration of every {{ meta.label.toLowerCase() }}: merged after the ancestors and before the
        annotation's own content.
      </p>
      <p v-if="affected !== undefined" class="text-xs text-dimmed">
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
    <EmptyState v-else-if="tab === 'versions'" icon="i-lucide-history" title="No template, no history" />

    <template v-else>
      <LoadingState v-if="template.isPending.value" />
      <EmptyState
        v-else-if="missing && draft === undefined"
        icon="i-lucide-layout-template"
        :title="`No ${meta.label.toLowerCase()} template`"
        description="Configurations of this type inherit only from their ancestors."
      >
        <UButton v-if="canEdit" label="Create template" icon="i-lucide-plus" @click="start" />
      </EmptyState>
      <ErrorState
        v-else-if="template.isError.value && !missing"
        :error="template.error.value"
        @retry="template.refetch()"
      />
      <template v-else>
        <div class="flex-1 p-4">
          <DocumentEditor
            :model-value="draft ?? {}"
            :format="format"
            :readonly="!canEdit"
            :extensions="[bindingDecorations()]"
            aria-label="Template"
            @update:model-value="(value) => (draft = value)"
            @update:format="(value) => (format = value)"
            @update:valid="(valid) => (textValid = valid)"
            @save="canEdit && dirty && save()"
          >
            <template #actions>
              <UBadge
                v-if="template.data.value?.Version"
                :label="`v${template.data.value.Version}`"
                color="neutral"
                variant="outline"
                size="sm"
              />
            </template>
          </DocumentEditor>
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
          <span v-if="dirty && !missing" class="text-xs text-warning">{{ changeCount(patch) }} change(s)</span>
          <UButton
            label="Revert"
            color="neutral"
            variant="ghost"
            :disabled="!dirty || saving"
            @click="draft = cloneJson(original)"
          />
          <UButton
            :label="missing ? 'Create' : 'Save'"
            :loading="saving"
            :disabled="!dirty || !textValid"
            @click="save()"
          />
        </div>
      </template>
    </template>

    <ConflictDialog
      v-model:open="conflictOpen"
      :theirs="theirs"
      :yours="draft"
      :loading="theirs === undefined"
      @reload="((original = cloneJson(theirs)), (draft = cloneJson(theirs)), (conflictOpen = false))"
      @overwrite="overwrite"
    />
    <ConfirmDialog
      v-model:open="confirmDelete"
      :title="`Delete the ${meta.label.toLowerCase()} template?`"
      description="Configurations of this type stop inheriting its values."
      :confirm-text="type"
      @confirm="remove"
    />
  </div>
</template>
