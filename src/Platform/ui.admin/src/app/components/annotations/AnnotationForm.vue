<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useForm } from 'vee-validate'
import { hasErrorCode, type Annotation } from '@42for.net/2splatform.sdk'
import {
  annotationFormSchema,
  annotationLimits,
  invalidAnnotationFields,
  readOnlyIssues,
  toAnnotationBody,
  toAnnotationDraft,
  type AnnotationForm,
} from '@/forms/annotation.schema'
import { toTypedSchema } from '@/forms/typed-schema'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore, type EditorMode } from '@/stores/preferences'
import { errorMessage } from '@/utils/errors'

/**
 * The Annotation tab (spec section 11.1): Form | JSON | YAML over one VeeValidate form with one Zod schema.
 * Text modes edit the whole annotation; key fields must stay unchanged.
 */
const props = defineProps<{ annotation: Annotation; fresh: boolean }>()

const route = useRoute()
const router = useRouter()
const context = useContextStore()
const preferences = usePreferencesStore()
const actions = useAnnotationActions()
const canEdit = useCan('edit')
const toast = useToast()

const { values, errors, meta, defineField, setValues, setFieldError, resetForm, handleSubmit, isSubmitting } =
  useForm<AnnotationForm>({
    validationSchema: toTypedSchema(annotationFormSchema),
    initialValues: toAnnotationDraft(props.annotation),
  })

const [title, titleAttrs] = defineField('Title')
const [description] = defineField('Description')
const [documentationLink, documentationLinkAttrs] = defineField('DocumentationLink')
const [isDisabled] = defineField('IsDisabled')
const [validFrom] = defineField('ValidFrom')
const [expiresAt] = defineField('ExpiresAt')
const [timeZone] = defineField('TimeZone')
const [labels] = defineField('Labels')
const [valuesField] = defineField('Values')

const readonly = computed(() => !canEdit.value)
const descriptionTab = ref<'write' | 'preview'>('write')
const textValid = ref(true)

// The mode is in ?format= when it differs from the preference (spec section 5).
const mode = computed<EditorMode>({
  get: () => {
    const fromQuery = route.query.format
    return fromQuery === 'form' || fromQuery === 'json' || fromQuery === 'yaml'
      ? fromQuery
      : preferences.state.annotationMode
  },
  set: (value) => {
    if (!textValid.value && mode.value !== 'form') {
      toast.add({ title: 'Fix the syntax error first', color: 'warning', icon: 'i-lucide-triangle-alert' })
      return
    }
    const query = { ...route.query, format: value === preferences.state.annotationMode ? undefined : value }
    void router.replace({ query })
  },
})

// New data (another key, a refetch) replaces the form unless the user has unsaved edits.
watch(
  () => props.annotation,
  (annotation, previous) => {
    if (!meta.value.dirty || annotation.AnnotationKey !== previous?.AnnotationKey) {
      resetForm({ values: toAnnotationDraft(annotation) })
    }
  },
)

useUnsavedChanges(() => meta.value.dirty)

const save = handleSubmit(async (draft) => {
  try {
    const saved = await actions.save(toAnnotationBody(draft as AnnotationForm, props.annotation))
    resetForm({ values: toAnnotationDraft(saved) })
    toast.add({ title: 'Annotation saved', color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    // 400 InvalidAnnotation names the field and its limit: the message goes to that field.
    if (hasErrorCode(error, 'InvalidAnnotation')) {
      for (const [field, message] of Object.entries(invalidAnnotationFields(error.message))) {
        setFieldError(field as keyof AnnotationForm, message)
      }
    }
    toast.add({ title: 'The annotation was not saved', description: errorMessage(error), color: 'error' })
  }
})

// The description editor counts the characters near the limit.
const descriptionLength = computed(() => description.value?.length ?? 0)
const showDescriptionCount = computed(() => descriptionLength.value > annotationLimits.Description * 0.9)
const countFormat = new Intl.NumberFormat('en-US')

const revert = () => resetForm({ values: toAnnotationDraft(props.annotation) })

const modes: EditorMode[] = ['form', 'json', 'yaml']
defineShortcuts({
  meta_s: { usingInput: true, handler: () => canEdit.value && meta.value.dirty && !isSubmitting.value && void save() },
  meta_shift_f: {
    usingInput: true,
    handler: () => (mode.value = modes[(modes.indexOf(mode.value) + 1) % modes.length]!),
  },
})

const errorList = computed(() => Object.entries(errors.value).filter(([, message]) => !!message))

const references = computed(() => {
  const record = props.annotation as unknown as Record<string, unknown>
  const keys = ['ResponsibilityKey', 'SubjectKey', 'UnitKey', 'UsageKey', 'ContextKey', 'ExecutionKey']
    .map((field) => ({ field: field.replace('Key', ''), key: record[field] }))
    .filter((entry): entry is { field: string; key: string } => typeof entry.key === 'string' && !!entry.key)
  const names = ['ResponsibilityNames', 'ContextNames', 'UnitNames']
    .map((field) => ({ field: field.replace('Names', ''), names: record[field] }))
    .filter(
      (entry): entry is { field: string; names: string[] } => Array.isArray(entry.names) && entry.names.length > 0,
    )
  const unit = ['UnitType', 'UnitDefinition']
    .map((field) => ({ field, value: record[field] }))
    .filter((entry): entry is { field: string; value: string } => typeof entry.value === 'string' && !!entry.value)
  return { keys, names, unit }
})

const link = (key: string) => ({
  path: `/${context.org}/${context.project}/${context.view}/annotations/${key}`,
  query: route.query,
})
</script>

<template>
  <div class="flex min-h-full flex-col">
    <div class="flex flex-wrap items-center gap-2 px-4 pt-4">
      <FormatToggle v-model="mode" with-form />
      <UBadge v-if="readonly" label="Read-only" icon="i-lucide-lock" color="neutral" variant="subtle" size="sm" />
      <UBadge
        v-if="!fresh"
        label="From the catalog"
        color="neutral"
        variant="outline"
        size="sm"
        title="Loading the current version…"
      />
    </div>

    <form v-if="mode === 'form'" class="flex-1 space-y-4 p-4" novalidate @submit.prevent="save">
      <!-- Name, type and key are part of the key: text, not inputs. The name is the key's last, bold segment. -->
      <p class="flex min-w-0 items-center gap-2" data-testid="annotation-identity">
        <AnnotationTypeBadge :type="annotation.AnnotationType" variant="icon" />
        <KeyText :value="annotation.AnnotationKey" size="sm" :truncate="false" />
      </p>

      <UFormField label="Title" name="Title" :error="errors.Title">
        <UInput v-model="title" v-bind="titleAttrs" :disabled="readonly" class="w-full" />
      </UFormField>

      <UFormField
        label="Description"
        name="Description"
        :error="errors.Description"
        :hint="
          showDescriptionCount
            ? `${countFormat.format(descriptionLength)} / ${countFormat.format(annotationLimits.Description)}`
            : undefined
        "
        help="Markdown: headings, lists, quotes, code blocks, tables and links."
      >
        <UTabs
          v-model="descriptionTab"
          :items="[
            { label: 'Write', value: 'write' },
            { label: 'Preview', value: 'preview' },
          ]"
          :content="false"
          size="xs"
          class="mb-2 w-48"
        />
        <UTextarea
          v-if="descriptionTab === 'write'"
          v-model="description"
          autoresize
          :rows="4"
          :disabled="readonly"
          class="w-full"
        />
        <MarkdownText
          v-else
          :source="description"
          empty="Nothing to preview."
          class="min-h-16 rounded-md border border-default p-3 text-sm"
        />
      </UFormField>

      <UFormField label="Documentation link" name="DocumentationLink" :error="errors.DocumentationLink">
        <UInput
          v-model="documentationLink"
          v-bind="documentationLinkAttrs"
          type="url"
          :disabled="readonly"
          class="w-full"
          placeholder="https://…"
        >
          <template v-if="documentationLink" #trailing>
            <UButton
              :to="documentationLink"
              target="_blank"
              icon="i-lucide-external-link"
              color="neutral"
              variant="link"
              size="xs"
              aria-label="Open the link"
            />
          </template>
        </UInput>
      </UFormField>

      <USwitch
        v-model="isDisabled"
        label="Disabled"
        description="A disabled annotation keeps its data but is not active."
        :disabled="readonly"
      />

      <div class="grid gap-4 lg:grid-cols-2">
        <UFormField label="Valid from" name="ValidFrom" :error="errors.ValidFrom">
          <ZonedDateTimeInput v-model="validFrom" label="Valid from" :time-zone="timeZone" :disabled="readonly" />
        </UFormField>
        <UFormField label="Expires" name="ExpiresAt" :error="errors.ExpiresAt">
          <ZonedDateTimeInput v-model="expiresAt" label="Expires" :time-zone="timeZone" :disabled="readonly" />
        </UFormField>
      </div>

      <UFormField
        label="Time zone"
        name="TimeZone"
        :error="errors.TimeZone"
        help="Dates are entered and shown in this zone."
      >
        <TimeZoneSelect v-model="timeZone" :disabled="readonly" />
      </UFormField>

      <UFormField label="Labels" name="Labels" :error="errors.Labels">
        <LabelsInput v-model="labels" :disabled="readonly" />
      </UFormField>

      <UFormField label="Values" name="Values" :error="errors.Values">
        <ValuesEditor v-model="valuesField" :disabled="readonly" />
      </UFormField>

      <div
        v-if="references.keys.length || references.names.length || references.unit.length"
        class="space-y-2 rounded-md border border-default p-3"
      >
        <p class="text-xs font-semibold text-muted uppercase">Relations</p>
        <div v-for="entry in references.keys" :key="entry.field" class="flex items-center gap-2 text-sm">
          <span class="w-24 text-muted">{{ entry.field }}</span>
          <RouterLink :to="link(entry.key)" class="font-mono text-xs text-primary hover:underline">{{
            entry.key
          }}</RouterLink>
        </div>
        <div v-for="entry in references.names" :key="entry.field" class="flex items-center gap-2 text-sm">
          <span class="w-24 text-muted">{{ entry.field }}s</span>
          <span class="flex flex-wrap gap-1"
            ><UBadge v-for="name in entry.names" :key="name" :label="name" color="neutral" variant="outline" size="sm"
          /></span>
        </div>
        <div v-for="entry in references.unit" :key="entry.field" class="flex items-center gap-2 text-sm">
          <span class="w-24 text-muted">{{ entry.field }}</span>
          <span class="font-mono text-xs">{{ entry.value }}</span>
        </div>
      </div>
    </form>

    <div v-else class="flex-1 p-4">
      <DocumentEditor
        :model-value="values"
        :format="mode === 'yaml' ? 'yaml' : 'json'"
        :readonly="readonly"
        :validate="(value) => readOnlyIssues(annotation, value)"
        hide-toggle
        aria-label="Annotation document"
        @update:model-value="(value) => setValues(value as AnnotationForm)"
        @update:valid="(valid) => (textValid = valid)"
        @save="canEdit && meta.dirty && !isSubmitting && save()"
      />
      <ul v-if="errorList.length" class="mt-2 space-y-0.5 text-xs text-error" role="alert">
        <li v-for="[path, message] in errorList" :key="path">
          <code class="font-mono">{{ path }}</code> {{ message }}
        </li>
      </ul>
    </div>

    <div
      v-if="canEdit"
      class="sticky bottom-0 flex items-center justify-end gap-2 border-t border-default bg-default/95 px-4 py-3 backdrop-blur"
    >
      <span v-if="meta.dirty" class="me-auto flex items-center gap-1 text-xs text-warning">
        <span class="size-1.5 rounded-full bg-warning" /> Unsaved changes
      </span>
      <UButton label="Revert" color="neutral" variant="ghost" :disabled="!meta.dirty || isSubmitting" @click="revert" />
      <UButton label="Save" :loading="isSubmitting" :disabled="!meta.dirty || !textValid" @click="save()">
        <template #trailing><UKbd value="meta" size="sm" /><UKbd value="S" size="sm" /></template>
      </UButton>
    </div>
  </div>
</template>
