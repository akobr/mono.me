<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQueryClient } from '@tanstack/vue-query'
import {
  deleteConfiguration,
  getTemplate,
  hasErrorCode,
  isConflict,
  isPreconditionFailed,
  patchConfiguration,
  setConfiguration,
} from '@42for.net/2splatform.sdk'
import { invalidateConfiguration } from '@/queries/invalidation'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore, type EditorMode } from '@/stores/preferences'
import { metaOf } from '@/utils/annotation-key'
import { cloneJson } from '@/utils/clone'
import { errorMessage } from '@/utils/errors'
import { buildPatch, changeCount } from '@/utils/json-patch'
import { parseServerErrors, type ValidationIssue } from '@/utils/schema-validation'
import { bindingDecorations } from '@/components/editor/setup/bindings'
import { schemaLinter } from '@/components/editor/setup/schema-lint'
import { provideBindingAssist, useBindingAssist } from '@/composables/useBindingAssist'

// The schema form (JSON Forms) loads on demand.
const SchemaForm = defineAsyncComponent(() => import('./SchemaForm.vue'))

/**
 * The Edit tab of a configuration (spec section 11.2): the own stored content as Form (when a schema applies),
 * JSON or YAML. Saving sends an RFC 6902 patch guarded with `test` operations; a 409 shows the schema errors (with
 * Save anyway), a 412 opens the conflict dialog. Inherited values come from the effective document.
 */
const props = defineProps<{ annotationKey: string }>()

const route = useRoute()
const router = useRouter()
const context = useContextStore()
const catalog = useCatalogStore()
const preferences = usePreferencesStore()
const queryClient = useQueryClient()
const confirm = useConfirm()
const toast = useToast()
const canEdit = useCan('edit')

const { effective, own, missing, noOwn, schema, version } = useConfiguration(() => props.annotationKey)

const original = ref<unknown>()
const draft = ref<unknown>()
const textValid = ref(true)
const formValid = ref(true)
const saving = ref(false)
const serverErrors = ref<ValidationIssue[]>([])
const conflictOpen = ref(false)
const theirs = ref<unknown>()
const confirmDelete = ref(false)
const creating = ref(false)

const dirty = computed(() => JSON.stringify(draft.value ?? null) !== JSON.stringify(original.value ?? null))
const patch = computed(() => (dirty.value ? buildPatch(original.value, draft.value) : []))

// The own content becomes the editor's base whenever it is (re)loaded and nothing is being edited.
watch(
  () => own.data.value?.Content,
  (content) => {
    if (content !== undefined && !dirty.value) {
      original.value = cloneJson(content)
      draft.value = cloneJson(content)
    }
  },
  { immediate: true },
)

useUnsavedChanges(dirty)

const mode = computed<EditorMode>({
  get: () => {
    const fromQuery = route.query.format
    if (fromQuery === 'form' || fromQuery === 'json' || fromQuery === 'yaml') {
      return fromQuery === 'form' && !schema.value ? preferences.state.editorFormat : fromQuery
    }
    return schema.value ? 'form' : preferences.state.editorFormat
  },
  set: (value) => {
    if (!textValid.value && mode.value !== 'form') {
      toast.add({ title: 'Fix the syntax error first', color: 'warning', icon: 'i-lucide-triangle-alert' })
      return
    }
    void router.replace({ query: { ...route.query, format: value } })
  },
})

// Completion, hover and the binding reference read the catalog, the paths of this configuration and the annotation.
const assist = useBindingAssist({
  annotationKey: () => props.annotationKey,
  documents: () => [effective.data.value?.Content, draft.value],
})
provideBindingAssist(assist)
const bindings = bindingDecorations(assist)

const extensions = computed(() =>
  schema.value && mode.value !== 'form' ? [schemaLinter(schema.value, mode.value), bindings] : [bindings],
)

async function refreshOwn(): Promise<unknown> {
  await effective.refetch()
  const result = await own.refetch()
  return result.data?.Content
}

async function save(force = false): Promise<void> {
  if (!context.scope || saving.value || patch.value.length === 0) {
    return
  }

  const { org, project, view } = context.scope
  saving.value = true
  serverErrors.value = []

  try {
    const saved = await patchConfiguration(
      org,
      project,
      view,
      props.annotationKey,
      patch.value,
      force ? { force: true } : undefined,
    )
    original.value = cloneJson(draft.value)
    catalog.setConfiguration(props.annotationKey, {
      AnnotationKey: props.annotationKey,
      Version: saved.Version,
      Modified: saved.Modified,
      HasContent: true,
    })
    await invalidateConfiguration(queryClient, context.scope, props.annotationKey)
    toast.add({ title: `Configuration saved (v${saved.Version ?? '?'})`, color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    if (isConflict(error) && error.schemaErrors) {
      serverErrors.value = parseServerErrors(error.schemaErrors, props.annotationKey)
    } else if (isPreconditionFailed(error) || hasErrorCode(error, 'PatchTestFailed')) {
      conflictOpen.value = true
      theirs.value = await refreshOwn().catch(() => undefined)
    } else {
      toast.add({ title: 'The configuration was not saved', description: errorMessage(error), color: 'error' })
    }
  } finally {
    saving.value = false
  }
}

async function forceSave() {
  const confirmed = await confirm({
    title: 'Save although the schema fails?',
    description: `The configuration will be stored with ${serverErrors.value.length} schema violation(s): ${serverErrors.value
      .slice(0, 3)
      .map((issue) => `${issue.pointer || '/'} ${issue.message}`)
      .join('; ')}.`,
    confirmLabel: 'Save anyway',
    color: 'error',
  })
  if (confirmed) {
    await save(true)
  }
}

function reloadTheirs() {
  original.value = cloneJson(theirs.value)
  draft.value = cloneJson(theirs.value)
  conflictOpen.value = false
}

async function overwrite() {
  // Re-patch from the fresh base to mine: the tests now guard the values just loaded.
  original.value = cloneJson(theirs.value)
  conflictOpen.value = false
  await save()
}

function revert() {
  draft.value = cloneJson(original.value)
  serverErrors.value = []
}

async function create(fromTemplate: boolean) {
  if (!context.scope) {
    return
  }
  const { org, project, view } = context.scope
  creating.value = true
  try {
    let content: Record<string, unknown> = {}
    if (fromTemplate) {
      const type = metaOf(props.annotationKey.slice(0, 3))?.code
      content = ((await getTemplate(org, project, view, type ?? '').catch(() => undefined))?.Content ?? {}) as Record<
        string,
        unknown
      >
    }
    const saved = await setConfiguration(org, project, view, props.annotationKey, content)
    catalog.setConfiguration(props.annotationKey, {
      AnnotationKey: props.annotationKey,
      Version: saved.Version,
      HasContent: true,
    })
    await invalidateConfiguration(queryClient, context.scope, props.annotationKey)
    toast.add({ title: 'Configuration created', color: 'success', icon: 'i-lucide-plus' })
  } catch (error) {
    toast.add({ title: 'The configuration was not created', description: errorMessage(error), color: 'error' })
  } finally {
    creating.value = false
  }
}

async function remove() {
  if (!context.scope) {
    return
  }
  const { org, project, view } = context.scope
  try {
    await deleteConfiguration(org, project, view, props.annotationKey)
    confirmDelete.value = false
    original.value = undefined
    draft.value = undefined
    catalog.setConfiguration(props.annotationKey, undefined)
    await invalidateConfiguration(queryClient, context.scope, props.annotationKey)
    toast.add({
      title: 'Own configuration deleted; inherited values remain',
      color: 'success',
      icon: 'i-lucide-trash-2',
    })
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
    <LoadingState v-if="effective.isPending.value" />

    <!-- Also when nothing is stored yet (own 404, effective Version 0): a patch would fail, the first content is created. -->
    <EmptyState
      v-else-if="missing || noOwn"
      icon="i-lucide-file-plus"
      title="No configuration yet"
      description="This annotation has no own configuration. It still inherits values from its ancestors and the type template."
    >
      <template v-if="canEdit">
        <UButton label="Create empty" icon="i-lucide-plus" :loading="creating" @click="create(false)" />
        <UButton
          label="Start from template"
          icon="i-lucide-layout-template"
          color="neutral"
          variant="outline"
          :loading="creating"
          @click="create(true)"
        />
      </template>
    </EmptyState>

    <ErrorState v-else-if="effective.isError.value" :error="effective.error.value" @retry="effective.refetch()" />

    <template v-else>
      <div class="flex flex-wrap items-center gap-2 px-4 pt-4">
        <FormatToggle v-model="mode" :with-form="!!schema" />
        <UBadge
          v-if="version !== undefined"
          :label="`v${version}`"
          color="neutral"
          variant="outline"
          size="sm"
          class="font-mono"
        />
        <UBadge v-if="!canEdit" label="Read-only" icon="i-lucide-lock" color="neutral" variant="subtle" size="sm" />
        <UTooltip v-if="!schema" text="No schema applies to this annotation, so there is no form.">
          <UIcon name="i-lucide-info" class="size-4 text-dimmed" />
        </UTooltip>
        <div class="ms-auto flex items-center gap-1">
          <BindingReference />
          <UButton
            v-if="canEdit"
            label="Delete own"
            icon="i-lucide-trash-2"
            size="xs"
            color="neutral"
            variant="ghost"
            @click="confirmDelete = true"
          />
        </div>
      </div>

      <div class="flex-1 space-y-3 p-4">
        <SchemaErrorsPanel
          v-if="serverErrors.length"
          :issues="serverErrors"
          :can-force="canEdit"
          :forcing="saving"
          @force="forceSave"
          @dismiss="serverErrors = []"
        />

        <LoadingState v-if="own.isPending.value && draft === undefined" />
        <SchemaForm
          v-else-if="mode === 'form' && schema"
          v-model="draft"
          :schema="schema"
          :effective="effective.data.value?.Content"
          :own="original"
          :readonly="!canEdit"
          :server-errors="serverErrors"
          @update:valid="(valid: boolean) => (formValid = valid)"
        />
        <DocumentEditor
          v-else
          :model-value="draft ?? {}"
          :format="mode === 'yaml' ? 'yaml' : 'json'"
          :readonly="!canEdit"
          :extensions="extensions"
          hide-toggle
          aria-label="Own configuration"
          @update:model-value="(value) => (draft = value)"
          @update:valid="(valid) => (textValid = valid)"
          @save="canEdit && dirty && save()"
        />
      </div>

      <div
        v-if="canEdit"
        class="sticky bottom-0 flex items-center justify-end gap-2 border-t border-default bg-default/95 px-4 py-3 backdrop-blur"
      >
        <span v-if="dirty" class="me-auto flex items-center gap-1 text-xs text-warning">
          <span class="size-1.5 rounded-full bg-warning" />
          {{ changeCount(patch) }} {{ changeCount(patch) === 1 ? 'change' : 'changes' }}
        </span>
        <span v-if="mode === 'form' && !formValid" class="text-xs text-error">The form has errors.</span>
        <UButton label="Revert" color="neutral" variant="ghost" :disabled="!dirty || saving" @click="revert" />
        <UButton label="Save" :loading="saving" :disabled="!dirty || !textValid" @click="save()">
          <template #trailing><UKbd value="meta" size="sm" /><UKbd value="S" size="sm" /></template>
        </UButton>
      </div>
    </template>

    <ConflictDialog
      v-model:open="conflictOpen"
      :theirs="theirs"
      :yours="draft"
      :loading="theirs === undefined"
      @reload="reloadTheirs"
      @overwrite="overwrite"
    />
    <ConfirmDialog
      v-model:open="confirmDelete"
      :title="`Delete the own configuration of ${annotationKey}?`"
      description="Only the own content is deleted; values inherited from ancestors and the template remain."
      :confirm-text="annotationKey"
      @confirm="remove"
    />
  </div>
</template>
