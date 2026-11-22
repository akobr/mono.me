<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { setAnnotation, type Annotation, type AnnotationMergePatch } from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { useProjectSettingsStore } from '@/stores/project-settings'
import {
  buildAnnotationKey,
  getAllAncestors,
  isValidName,
  typeCodes,
  typeMeta,
  type AnnotationKeyParts,
  type AnnotationTypeCode,
} from '@/utils/annotation-key'
import { withRelations } from '@/utils/annotation-relations'
import { errorMessage, writeFailureTitle } from '@/utils/errors'

/**
 * New annotation (spec section 8): pick a type, fill the parent names it needs (suggested from the catalog,
 * new names allowed) and the name, with a live key preview. SetAnnotation creates a new key with its missing
 * ancestors (CosmosAnnotationService.TryCheckAndCreate*), so the dialog only lists them; the body carries the
 * relation members the runtime type requires, derived from the key. A deleted key or ancestor is looked up first
 * (useDeletedKeyCheck): the user restores it, which keeps its configuration and history, and only the dialog's title
 * and zone are patched over it; or creates it new.
 * The time zone starts as the project's default (project settings, 424-12); the API copies it to the new ancestors.
 */
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [key: string] }>()

const catalog = useCatalogStore()
const context = useContextStore()
const projectSettings = useProjectSettingsStore()
const actions = useAnnotationActions()
const deletedCheck = useDeletedKeyCheck()
const toast = useToast()

type Part = keyof AnnotationKeyParts

const type = ref<AnnotationTypeCode>('exe')
const parts = reactive<Record<Part, string>>({ subject: '', responsibility: '', context: '', unit: '', name: '' })
const title = ref('')
const timeZone = ref(projectSettings.defaultTimeZone)
// Whether the zone was picked in this opening of the dialog: until then it follows the project's default, so a dialog
// opened before the project settings arrive from the API moves to their zone (424-266).
let timeZoneTouched = false
const saving = ref(false)
const failure = ref<string>()

watch(open, (value) => {
  if (value) {
    failure.value = undefined
    timeZone.value = projectSettings.defaultTimeZone
    timeZoneTouched = false
  }
})

watch(
  () => projectSettings.defaultTimeZone,
  (zone) => {
    if (open.value && !timeZoneTouched) {
      timeZone.value = zone
    }
  },
)

function pickTimeZone(zone: string) {
  timeZone.value = zone
  timeZoneTouched = true
}

// Computed: the type names and icons follow the project settings and Preferences (424-13).
const typeItems = computed(() =>
  typeCodes.map((code) => ({ label: typeMeta[code].label, value: code, icon: typeMeta[code].icon })),
)
const meta = computed(() => typeMeta[type.value])

const partLabels = computed<Record<Part, string>>(() => ({
  subject: typeMeta.sbt.label,
  responsibility: typeMeta.rst.label,
  context: typeMeta.cnt.label,
  unit: typeMeta.unt.label,
  name: 'Name',
}))

/** Existing names per part, from the catalog (contexts and units only within the chosen subject/responsibility). */
const suggestions = computed<Record<Part, string[]>>(() => {
  const names = (code: AnnotationTypeCode, filter?: (key: string) => boolean) =>
    [...catalog.byKey.keys()]
      .filter((key) => key.startsWith(`${code}.`) && (!filter || filter(key)))
      .map((key) => key.split('.').at(-1)!)
  return {
    subject: names('sbt'),
    responsibility: names('rst'),
    context: names('cnt', (key) => !parts.subject || key.startsWith(`cnt.${parts.subject}.`)),
    unit: names('unt', (key) => !parts.responsibility || key.startsWith(`unt.${parts.responsibility}.`)),
    name: [],
  }
})

const keyParts = computed<AnnotationKeyParts>(() => {
  const result: AnnotationKeyParts = {}
  for (const part of meta.value.parts) {
    result[part] = parts[part].trim()
  }
  return result
})

const partErrors = computed(
  () =>
    Object.fromEntries(
      meta.value.parts.map((part) => [
        part,
        parts[part].trim() && !isValidName(parts[part].trim()) ? 'Names cannot contain dots.' : undefined,
      ]),
    ) as Record<Part, string | undefined>,
)

const key = computed(() => {
  try {
    return buildAnnotationKey(type.value, keyParts.value)
  } catch {
    return undefined
  }
})

const exists = computed(() => !!key.value && catalog.byKey.has(key.value))
const missingAncestors = computed(() =>
  key.value ? getAllAncestors(key.value).filter((ancestor) => !catalog.byKey.has(ancestor)) : [],
)

async function create() {
  if (!key.value || exists.value || !context.scope) {
    return
  }

  saving.value = true
  failure.value = undefined
  const { org, project, view } = context.scope

  try {
    const { choice, deleted } = await deletedCheck.check(key.value)
    if (choice === 'cancel') {
      return
    }

    // Restore and update: the deleted keys come back alone (their deleted children stay deleted), and a restored
    // key keeps its content; only what the dialog sets is patched over it (424-225).
    let restored: Annotation | undefined
    if (choice === 'restore') {
      const outcome = await actions.restoreMany(
        deleted.map((annotation: Annotation) => annotation.AnnotationKey),
        { descendants: false },
      )
      if (outcome.failed.length) {
        throw new Error(outcome.failed.map((failed) => `${failed.Key}: ${failed.Message}`).join(' '))
      }
      restored = outcome.restored.find((annotation) => annotation.AnnotationKey === key.value)
    }

    if (restored) {
      await updateRestored(restored)
      return
    }

    // A new key, Create new over a deleted one, or a key whose deleted ancestors were restored: only a whole write
    // (POST) creates or re-creates a key.
    const created = await setAnnotation(
      org,
      project,
      view,
      key.value,
      withRelations({
        ProjectName: project,
        ViewName: view,
        AnnotationKey: key.value,
        Name: key.value.split('.').at(-1)!,
        AnnotationType: meta.value.type,
        Title: title.value.trim() || undefined,
        TimeZone: timeZone.value || undefined,
      }),
    )
    catalog.upsert(created)
    // The API created (or re-created) the missing ancestors; a background refresh brings them into the catalog.
    if (missingAncestors.value.length) {
      void catalog.load(context.scope, { force: true })
    }
    toast.add({ title: `${created.AnnotationKey} created`, color: 'success', icon: 'i-lucide-plus' })
    finish(created.AnnotationKey)
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    saving.value = false
  }
}

/**
 * A merge patch of what the dialog sets over the restored record: the title when it is given and differs, and the
 * time zone when the record has none (a restored annotation keeps its own zone). An empty patch writes nothing. The key
 * is restored whether the patch succeeds or not, so the dialog closes on it either way.
 */
async function updateRestored(restored: Annotation) {
  const restoredKey = restored.AnnotationKey
  const patch: AnnotationMergePatch = {}
  const newTitle = title.value.trim()
  if (newTitle && newTitle !== restored.Title) {
    patch.Title = newTitle
  }
  if (!restored.TimeZone && timeZone.value) {
    patch.TimeZone = timeZone.value
  }

  try {
    if (Object.keys(patch).length) {
      await actions.patch(restoredKey, patch)
    }
    toast.add({ title: `${restoredKey} restored and updated`, color: 'success', icon: 'i-lucide-archive-restore' })
  } catch (error) {
    toast.add({
      title: writeFailureTitle(error, `${restoredKey} restored, but not updated`),
      description: errorMessage(error),
      color: 'warning',
    })
  }
  finish(restoredKey)
}

function finish(annotationKey: string) {
  open.value = false
  emit('created', annotationKey)
  parts.name = ''
  title.value = ''
}
</script>

<template>
  <UModal v-model:open="open" title="New annotation" :ui="{ content: 'max-w-xl' }">
    <template #body>
      <form class="space-y-4" novalidate @submit.prevent="create">
        <UFormField label="Type" required>
          <USelect v-model="type" :items="typeItems" class="w-full" />
        </UFormField>

        <div class="grid gap-3 sm:grid-cols-2">
          <UFormField
            v-for="part in meta.parts"
            :key="`${type}:${part}`"
            :label="partLabels[part]"
            required
            :error="partErrors[part]"
          >
            <UInputMenu
              v-if="part !== 'name'"
              v-model="parts[part]"
              :items="suggestions[part]"
              create-item
              class="w-full"
              :placeholder="`Existing or new ${partLabels[part].toLowerCase()}`"
              @create="(value: string) => (parts[part] = value)"
            />
            <UInput v-else v-model="parts.name" class="w-full" autocomplete="off" />
          </UFormField>
        </div>

        <UFormField label="Title">
          <UInput v-model="title" class="w-full" />
        </UFormField>

        <UFormField label="Time zone" help="Default from the project settings.">
          <TimeZoneSelect :model-value="timeZone" @update:model-value="pickTimeZone" />
        </UFormField>

        <div class="rounded-md border border-default bg-muted p-3 text-sm">
          <p class="text-xs text-muted">Key</p>
          <KeyText v-if="key" :value="key" :truncate="false" />
          <span v-else class="text-dimmed">Fill in the names</span>
          <p v-if="exists" class="mt-1 text-xs text-error">This annotation already exists.</p>
          <div v-if="missingAncestors.length && !exists" class="mt-2 text-xs text-muted">
            The API also creates the missing ancestors:
            <span class="font-mono">{{ missingAncestors.join(', ') }}</span>
          </div>
        </div>

        <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />

        <DeletedKeysDialog
          v-model:open="deletedCheck.open.value"
          :deleted="deletedCheck.deleted.value"
          @choose="deletedCheck.choose"
        />

        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton type="submit" label="Create" :loading="saving" :disabled="!key || exists" />
        </div>
      </form>
    </template>
  </UModal>
</template>
