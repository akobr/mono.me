<script setup lang="ts">
import { computed, ref, shallowRef, toRaw, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getCertificateAuthority,
  hasErrorCode,
  isPreconditionFailed,
  isStorytellerError,
  projectSettingsEntityTag,
  setMachineAuthentication,
  setProjectSettings,
  updateAccessPoint,
  type MachineCredentialKind,
  type ProjectSettings,
} from '@42for.net/2splatform.sdk'
import {
  getGetProjectSettingsQueryKey,
  useGetAccessPoint,
  useGetMachineAuthentication,
} from '@42for.net/2splatform.sdk/vue-query'
import { descriptionSchema } from '@/forms/names.schema'
import { useExhibition } from '@/composables/useExhibition'
import { invalidateAccess, invalidateProject } from '@/queries/invalidation'
import { useProjectSettingsQuery } from '@/queries/project-settings'
import { useProjectSummaries } from '@/queries/projects'
import { useKeyValuePolicy } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { dateFormatItems, defaultDateFormat, type DateFormat } from '@/utils/dates'
import { errorMessage, writeFailureTitle } from '@/utils/errors'
import {
  legacyProjectSettingsDraft,
  readLegacyProjectSettings,
  removeLegacyProjectSettings,
  type LegacyProjectSettings,
} from '@/utils/legacy-project-settings'
import { credentialKindDescriptions, credentialKinds, issuesCertificates } from '@/utils/machines'
import {
  defaultTimeZone as builtInTimeZone,
  fromApi,
  toApi,
  type ProjectSettingsDraft,
  type ProjectSettingsState,
} from '@/utils/project-settings'
import { ownerCount } from '@/utils/roles'
import { resolveTypeAppearances, type TypeAppearances } from '@/utils/type-appearance'

// Project settings (spec section 13): the machine authentication policy (Administrator: the enabled credential
// kinds, the default kind and certificate lifetime of new machines, 424-14), the CA certificate download (PEM; the
// SDK fetcher returns non-JSON bodies as text) and project information with the description of the project (every
// member reads it, Administrators change it, 424-78). The default time zone of new annotations (424-12), the date
// format (424-23) and the names, colors and icons of the annotation types (424-13) are the project's settings on the
// server, for everyone in the project (424-266): every member reads them, Administrators change them in one draft with
// one Save. The secrets settings of the old Secrets page (spec 29.6: allowed levels, the keys, the customer key and the
// recipients) are the last section since 424-35; every reader sees them, Administrators change them.
const context = useContextStore()
const preferences = usePreferencesStore()
const queryClient = useQueryClient()
const toast = useToast()
const role = useRole()
const canAdmin = useCan('manageAccess')
const canManageSettings = useCan('manageProject')

// The saved settings, as the API answered them (the defaults until the first answer), and the draft of the three
// settings: it follows the saved ones until it is changed, and starts again from them in another project. The rest of
// the app keeps the saved settings until Save; only the previews of the type editor show the draft.
const settingsQuery = useProjectSettingsQuery()
/**
 * The answer of the settings query when it is the current project's. The page stays on a project switch (the context
 * switcher keeps the section), and for a moment the query still holds the previous project's answer: vue-query moves
 * to the new key in a watcher of its own, which may run after the watchers of this page. Only the answer cached under
 * the current project's key counts, so nothing is decided on another project's settings.
 */
const settingsAnswer = computed<ProjectSettings | undefined>(() => {
  const data = settingsQuery.data.value
  const { org, project } = context
  return data && org && project && toRaw(data) === queryClient.getQueryData(getGetProjectSettingsQueryKey(org, project))
    ? data
    : undefined
})
const settingsLoaded = computed(() => !!settingsAnswer.value)
const savedSettings = computed(() => fromApi(settingsAnswer.value))

const draftOf = (settings: ProjectSettingsDraft): ProjectSettingsDraft => ({
  types: structuredClone({ ...settings.types }),
  defaultTimeZone: settings.defaultTimeZone,
  dateFormat: settings.dateFormat,
})
/** Whether a draft holds the same settings as saved ones: the same body of a PUT. */
const sameSettings = (value: ProjectSettingsDraft, saved: ProjectSettingsState) =>
  JSON.stringify(toApi(value, saved)) === JSON.stringify(toApi(saved, saved))

const savingSettings = ref(false)
// A 412: someone saved the settings since they were read; they are read again, the draft stays.
const settingsConflict = ref(false)
// Whether the settings were read again after the 412: until then, the settings this page holds are the old ones, so
// they cannot be loaded as the new ones, and a failed read offers a retry.
const settingsConflictRead = ref(false)
const readingConflictSettings = ref(false)
// A 400 InvalidSettings: the problems the API found, each with its JSON path.
const settingsProblems = ref<{ path?: string; message: string }[]>()

const draft = shallowRef<ProjectSettingsDraft>(draftOf(savedSettings.value))
// The saved settings the draft started from; Save sends their version as If-Match. A newer answer under a changed
// draft (someone else saved, and the query read the settings again, e.g. after a reconnect) moves neither the draft nor
// this base, so Save gets a 412 and says so instead of storing the draft over that change. After the 412 the base is
// the newer answer: a second Save then stores the draft over it.
const draftBase = shallowRef<ProjectSettingsState>(savedSettings.value)
function startDraft(saved: ProjectSettingsState) {
  draft.value = draftOf(saved)
  draftBase.value = saved
}
watch(
  [() => context.projectKey, savedSettings] as const,
  ([key, saved], [previousKey]) => {
    if (key !== previousKey || sameSettings(draft.value, draftBase.value) || sameSettings(draft.value, saved)) {
      startDraft(saved)
    }
    if (key !== previousKey) {
      settingsConflict.value = false
      settingsConflictRead.value = false
      settingsProblems.value = undefined
    }
  },
  { immediate: true },
)
// Only a draft that can still be saved is unsaved: after Leave project (424-271) the role is gone, and the navigation
// away must not ask about it.
const settingsDirty = computed(
  () => canManageSettings.value && settingsLoaded.value && !sameSettings(draft.value, savedSettings.value),
)
const settingsEditable = computed(() => canManageSettings.value && settingsLoaded.value)
useUnsavedChanges(settingsDirty, { message: 'Your changes of the project settings have not been saved.' })

const defaultTimeZone = computed({
  get: () => draft.value.defaultTimeZone,
  set: (value: string) => {
    if (value) {
      draft.value = { ...draft.value, defaultTimeZone: value }
    }
  },
})

const dateFormat = computed({
  get: () => draft.value.dateFormat,
  set: (value: DateFormat) => (draft.value = { ...draft.value, dateFormat: value }),
})
const dateFormatOptions = dateFormatItems()

const types = computed({
  get: () => draft.value.types,
  set: (value: TypeAppearances) => (draft.value = { ...draft.value, types: value }),
})
const builtInTypes = resolveTypeAppearances()
const hasPersonalTypes = computed(() => Object.keys(preferences.types).length > 0)

/** The problems of a 400 InvalidSettings (Details: Path, Rule, Message), or its message when it lists none. */
function problemsOf(error: unknown): { path?: string; message: string }[] {
  const details = isStorytellerError(error) ? (error.body as { Details?: unknown } | undefined)?.Details : undefined
  const problems = Array.isArray(details)
    ? details
        .filter((detail): detail is { Path?: unknown; Message?: unknown } => !!detail && typeof detail === 'object')
        .map((detail) => ({
          path: typeof detail.Path === 'string' ? detail.Path : undefined,
          message: typeof detail.Message === 'string' ? detail.Message : '',
        }))
        .filter((problem) => problem.message)
    : []
  return problems.length > 0 ? problems : [{ message: errorMessage(error) }]
}

async function saveSettings() {
  const { org, project, projectKey } = context
  if (!org || !project || !projectKey || !settingsDirty.value || savingSettings.value) {
    return
  }
  const base = draftBase.value
  const sent = draft.value
  savingSettings.value = true
  settingsConflict.value = false
  settingsConflictRead.value = false
  settingsProblems.value = undefined
  try {
    // A browser cannot read the ETag of another origin; the Version of the body makes the same tag.
    const answer = await setProjectSettings(org, project, toApi(sent, base), {
      headers: { 'If-Match': projectSettingsEntityTag(base.version) },
    })
    // A read still on its way would put the settings before this save back into the cache.
    await queryClient.cancelQueries({ queryKey: getGetProjectSettingsQueryKey(org, project) })
    queryClient.setQueryData(getGetProjectSettingsQueryKey(org, project), answer)
    // The fields show what is stored, unless the user went on editing; further changes start from the stored settings.
    if (context.projectKey === projectKey) {
      const stored = fromApi(answer)
      if (draft.value === sent) {
        startDraft(stored)
      } else {
        draftBase.value = stored
      }
    }
    toast.add({ title: 'Project settings saved', color: 'success', icon: 'i-lucide-check' })
    // The list of the caller's projects and the access point carry the settings too.
    await invalidateProject(queryClient, projectKey)
  } catch (error) {
    if (hasErrorCode(error, 'SettingsPreconditionFailed') || isPreconditionFailed(error)) {
      settingsConflict.value = true
      await readConflictSettings()
    } else if (hasErrorCode(error, 'InvalidSettings')) {
      settingsProblems.value = problemsOf(error)
    } else {
      toast.add({
        title: writeFailureTitle(error, 'The project settings were not saved'),
        description: errorMessage(error),
        color: 'error',
      })
    }
  } finally {
    savingSettings.value = false
  }
}

/**
 * After a 412: reads the settings again. Only a read that succeeds makes them the base of the draft, so a second Save
 * stores the draft over them, and lets the user load them; a failed read keeps the conflict and offers a retry.
 */
async function readConflictSettings() {
  const { org, project, projectKey } = context
  if (!org || !project || !projectKey || readingConflictSettings.value) {
    return
  }
  readingConflictSettings.value = true
  try {
    const result = await settingsQuery.refetch()
    const latest = queryClient.getQueryData<ProjectSettings>(getGetProjectSettingsQueryKey(org, project))
    if (!result.isError && latest && context.projectKey === projectKey && settingsConflict.value) {
      draftBase.value = fromApi(latest)
      settingsConflictRead.value = true
    }
  } finally {
    readingConflictSettings.value = false
  }
}

/** After a 412 and a read of the new settings: the draft starts again from them. */
function loadSavedSettings() {
  if (!settingsConflictRead.value) {
    return
  }
  startDraft(savedSettings.value)
  settingsConflict.value = false
  settingsConflictRead.value = false
  settingsProblems.value = undefined
}

// The settings this browser kept for the project before the API stored them (424-266, spec section 4): while the
// project has none on the server (Version 0), an Administrator can save them for everyone or discard them, and anyone
// else is asked to have an Administrator save them. Once the server has settings, useCurrentProjectSettings removes
// the entry, keyed by the query of each answer; this page only reads it again with each answer of the current project,
// never removes it on the strength of an answer that may still be another project's.
const legacySettings = shallowRef<LegacyProjectSettings>()
watch(
  [() => context.projectKey, settingsAnswer] as const,
  ([key]) => {
    legacySettings.value = key ? readLegacyProjectSettings(key) : undefined
  },
  { immediate: true },
)
const showLegacySettings = computed(
  () => !!legacySettings.value && settingsLoaded.value && savedSettings.value.version === 0,
)
/** What the old settings change, for the banner. */
const legacySummary = computed(() => {
  const settings = legacySettings.value
  if (!settings) {
    return ''
  }
  const parts: string[] = []
  if (settings.defaultTimeZone) {
    parts.push(`time zone ${settings.defaultTimeZone}`)
  }
  if (settings.dateFormat) {
    parts.push(`date format ${settings.dateFormat}`)
  }
  const typeCount = Object.keys(settings.types ?? {}).length
  if (typeCount > 0) {
    parts.push(`names, colors or icons of ${typeCount} annotation ${typeCount === 1 ? 'type' : 'types'}`)
  }
  const text = parts.join(', ')
  return `${text.charAt(0).toUpperCase()}${text.slice(1)}.`
})
const savingLegacySettings = ref(false)

/** Saves the old settings of this browser for the project, under If-Match "0": only while it has none on the server. */
async function saveLegacySettings() {
  const { org, project, projectKey } = context
  const settings = legacySettings.value
  if (!org || !project || !projectKey || !settings || savingLegacySettings.value) {
    return
  }
  savingLegacySettings.value = true
  try {
    const answer = await setProjectSettings(
      org,
      project,
      toApi(legacyProjectSettingsDraft(settings), savedSettings.value),
      { headers: { 'If-Match': projectSettingsEntityTag(0) } },
    )
    removeLegacyProjectSettings(projectKey)
    // Another project may be open by now: its offer stays.
    if (context.projectKey === projectKey) {
      legacySettings.value = undefined
    }
    await queryClient.cancelQueries({ queryKey: getGetProjectSettingsQueryKey(org, project) })
    queryClient.setQueryData(getGetProjectSettingsQueryKey(org, project), answer)
    toast.add({ title: 'Project settings saved', color: 'success', icon: 'i-lucide-check' })
    await invalidateProject(queryClient, projectKey)
  } catch (error) {
    toast.add({
      title: writeFailureTitle(error, 'The project settings were not saved'),
      description: errorMessage(error),
      color: 'error',
    })
    // Someone saved settings meanwhile: their answer removes the entry.
    if (hasErrorCode(error, 'SettingsPreconditionFailed') || isPreconditionFailed(error)) {
      await settingsQuery.refetch()
    }
  } finally {
    savingLegacySettings.value = false
  }
}

function discardLegacySettings() {
  if (context.projectKey) {
    removeLegacyProjectSettings(context.projectKey)
  }
  legacySettings.value = undefined
}

const accessPoint = useGetAccessPoint(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canAdmin.value, meta: { silent: true } },
})

// The policy as the API resolves it: every property is filled, deployment defaults included.
const machinePolicy = useGetMachineAuthentication(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canAdmin.value, meta: { silent: true } },
})

const projects = useProjectSummaries()
const savedDescription = computed(() => projects.summaryOf(context.projectKey)?.Description ?? '')

// Leave project (424-271), for every member: the only Owner may not. Only an Owner can be that, and an Owner reads the
// access point; until it is loaded the API decides (409 LastOwner).
const exhibition = useExhibition()
const comeBack = (isExhibition: boolean | undefined) =>
  isExhibition === undefined ? undefined : isExhibition ? ('exhibition' as const) : ('invitation' as const)
const lastOwner = computed(
  () => role.projectRole.value === 'Owner' && (ownerCount(accessPoint.data.value?.AccessMap) ?? Infinity) <= 1,
)

const secretsPolicy = useKeyValuePolicy()
const recipients = computed(() => secretsPolicy.data.value?.Recipients ?? [])

// #secrets (the old Secrets page and the BYOK banner link here) scrolls once the cards above it have their final size.
useHashScroll(
  () =>
    !projects.isPending.value &&
    !settingsQuery.isPending.value &&
    !secretsPolicy.isPending.value &&
    !(canAdmin.value && (machinePolicy.isPending.value || accessPoint.isPending.value)),
)

// The text follows the loaded description until the user changes it; another project starts again from its own.
const description = ref('')
watch(
  [() => context.projectKey, savedDescription] as const,
  ([key, value], [previousKey, previous]) => {
    if (key !== previousKey || description.value.trim() === (previous ?? '')) {
      description.value = value
    }
  },
  { immediate: true },
)
const descriptionError = computed(() => descriptionSchema.safeParse(description.value).error?.issues[0]?.message)
const descriptionChanged = computed(() => description.value.trim() !== savedDescription.value)
const savingDescription = ref(false)

async function saveDescription() {
  if (!descriptionChanged.value || descriptionError.value) {
    return
  }
  savingDescription.value = true
  const key = context.projectKey!
  const text = description.value.trim()
  try {
    await updateAccessPoint(key, { Description: text || undefined })
    // The text shows what is stored, unless the user went on typing.
    if (description.value.trim() === text) {
      description.value = text
    }
    await invalidateProject(queryClient, key)
    toast.add({ title: 'Description saved', color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    toast.add({ title: 'The description was not saved', description: errorMessage(error), color: 'error' })
  } finally {
    savingDescription.value = false
  }
}

const enabledKinds = ref<MachineCredentialKind[]>(['ApiKey'])
const defaultKind = ref<MachineCredentialKind>('ApiKey')
const lifetime = ref(365)
watch(
  () => machinePolicy.data.value,
  (value) => {
    if (value) {
      enabledKinds.value = [...(value.EnabledCredentialKinds ?? [])]
      defaultKind.value = value.CredentialKind ?? enabledKinds.value[0] ?? 'ApiKey'
      lifetime.value = value.CertificateLifetimeDays ?? lifetime.value
    }
  },
  { immediate: true },
)

// The default is one of the enabled kinds; unchecking it moves the default to the first enabled kind.
watch(enabledKinds, (kinds) => {
  if (kinds.length > 0 && !kinds.includes(defaultKind.value)) {
    defaultKind.value = credentialKinds.find((kind) => kinds.includes(kind))!
  }
})

const saving = ref(false)
const kindItems = credentialKinds.map((kind) => ({
  label: kind,
  value: kind,
  description: credentialKindDescriptions[kind],
}))
const defaultKindItems = computed(() => credentialKinds.filter((kind) => enabledKinds.value.includes(kind)))
const anyCertificates = computed(() => enabledKinds.value.some(issuesCertificates))

async function savePolicy() {
  if (enabledKinds.value.length === 0) {
    return
  }
  saving.value = true
  try {
    await setMachineAuthentication(context.projectKey!, {
      EnabledCredentialKinds: credentialKinds.filter((kind) => enabledKinds.value.includes(kind)),
      CredentialKind: defaultKind.value,
      CertificateLifetimeDays: anyCertificates.value ? lifetime.value : null,
    })
    await invalidateAccess(queryClient, context.projectKey!, context.org, context.project)
    toast.add({ title: 'Machine policy saved', color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    toast.add({ title: 'The policy was not saved', description: errorMessage(error), color: 'error' })
  } finally {
    saving.value = false
  }
}

async function downloadCa() {
  try {
    const pem = await getCertificateAuthority()
    const url = URL.createObjectURL(new Blob([pem], { type: 'application/x-pem-file' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'storyteller-ca.pem'
    link.click()
    URL.revokeObjectURL(url)
  } catch (error) {
    toast.add({ title: 'The CA certificate could not be downloaded', description: errorMessage(error), color: 'error' })
  }
}
</script>

<template>
  <UDashboardPanel id="settings">
    <template #header>
      <AppNavbar title="Settings" />
    </template>
    <template #body>
      <div class="mx-auto w-full max-w-3xl space-y-6">
        <UCard>
          <template #header><h2 class="font-semibold">Project</h2></template>
          <dl class="grid grid-cols-[10rem_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
            <dt class="text-muted">Access point</dt>
            <dd class="font-mono">{{ context.projectKey }}</dd>
            <dt class="text-muted">Your role</dt>
            <dd><RoleBadge :role="role.projectRole.value" /></dd>
            <template v-if="accessPoint.data.value">
              <dt class="text-muted">Members</dt>
              <dd>{{ Object.keys(accessPoint.data.value.AccessMap ?? {}).length }}</dd>
            </template>
          </dl>
          <!-- At full width under the list: its label column leaves little room on a phone. -->
          <LoadingState v-if="projects.isPending.value" />
          <ErrorState
            v-else-if="projects.isError.value"
            :error="projects.error.value"
            class="mt-4"
            compact
            @retry="projects.refetch()"
          />
          <form v-else-if="canAdmin" class="mt-4 space-y-4" @submit.prevent="saveDescription">
            <UFormField label="Description" :error="descriptionError">
              <UTextarea v-model="description" class="w-full" autoresize />
            </UFormField>
            <div class="flex justify-end">
              <UButton
                type="submit"
                label="Save description"
                :loading="savingDescription"
                :disabled="!descriptionChanged || !!descriptionError"
              />
            </div>
          </form>
          <dl v-else class="mt-2 space-y-1 text-sm">
            <dt class="text-muted">Description</dt>
            <dd v-if="savedDescription" class="break-words whitespace-pre-line">{{ savedDescription }}</dd>
            <dd v-else class="text-muted">No description</dd>
          </dl>
          <!-- Stays while a leave ends: the role is gone before the page is (424-271). -->
          <template v-if="context.projectKey" #footer>
            <ProjectLeave
              :project-key="context.projectKey"
              :role="role.projectRole.value"
              :last-owner="lastOwner"
              :come-back="comeBack(exhibition.isExhibition(context.projectKey))"
            />
          </template>
        </UCard>

        <ErrorState
          v-if="settingsQuery.isError.value && !settingsLoaded"
          :error="settingsQuery.error.value"
          title="The project settings could not be loaded"
          compact
          @retry="settingsQuery.refetch()"
        />

        <UAlert
          v-if="showLegacySettings"
          color="info"
          variant="subtle"
          icon="i-lucide-upload"
          title="Project settings of this browser"
          :actions="
            canManageSettings
              ? [
                  {
                    label: 'Save for the project',
                    color: 'info',
                    loading: savingLegacySettings,
                    onClick: saveLegacySettings,
                  },
                  {
                    label: 'Discard',
                    color: 'neutral',
                    variant: 'outline',
                    disabled: savingLegacySettings,
                    onClick: discardLegacySettings,
                  },
                ]
              : []
          "
        >
          <template #description>
            <p v-if="canManageSettings">
              These project settings were set in this browser before they were shared. Save them for the whole project?
            </p>
            <p v-else>
              These project settings were set in this browser before they were shared. Ask an Administrator of the
              project to save them for everyone.
            </p>
            <p class="mt-1 text-xs">{{ legacySummary }}</p>
          </template>
        </UAlert>

        <UCard>
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <h2 class="font-semibold">Annotations</h2>
              <UBadge
                v-if="!canManageSettings"
                label="Administrator"
                icon="i-lucide-lock"
                color="neutral"
                variant="subtle"
                size="sm"
              />
            </div>
            <p v-if="!canManageSettings" class="mt-1 text-sm text-muted">
              Only Administrators change the project settings.
            </p>
          </template>
          <UFormField
            label="Default time zone"
            help="New annotations start in this time zone, for everyone in this project."
          >
            <template v-if="canManageSettings" #hint>
              <UButton
                label="Use the default"
                aria-label="Use the default: UTC"
                color="neutral"
                variant="link"
                size="xs"
                :disabled="!settingsEditable || defaultTimeZone === builtInTimeZone"
                @click="defaultTimeZone = builtInTimeZone"
              />
            </template>
            <TimeZoneSelect
              v-model="defaultTimeZone"
              :disabled="!settingsEditable"
              aria-label="Default time zone"
              class="max-w-sm"
            />
          </UFormField>
        </UCard>

        <UCard>
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <h2 class="font-semibold">Dates</h2>
              <UBadge
                v-if="!canManageSettings"
                label="Administrator"
                icon="i-lucide-lock"
                color="neutral"
                variant="subtle"
                size="sm"
              />
            </div>
            <p v-if="!canManageSettings" class="mt-1 text-sm text-muted">
              Only Administrators change the project settings.
            </p>
          </template>
          <UFormField
            label="Date format"
            help="How dates are shown on every page, for everyone in this project. Each person can pick their own format in Preferences."
          >
            <template v-if="canManageSettings" #hint>
              <UButton
                label="Use the default"
                aria-label="Use the default: yyyy-mm-dd"
                color="neutral"
                variant="link"
                size="xs"
                :disabled="!settingsEditable || dateFormat === defaultDateFormat"
                @click="dateFormat = defaultDateFormat"
              />
            </template>
            <USelect
              v-model="dateFormat"
              :items="dateFormatOptions"
              :disabled="!settingsEditable"
              class="w-full max-w-sm"
            />
          </UFormField>
          <UAlert
            v-if="preferences.dateFormat"
            class="mt-4"
            color="neutral"
            variant="subtle"
            icon="i-lucide-user-cog"
            :description="`Your own Preferences show dates as ${preferences.dateFormat}.`"
            :actions="[{ label: 'Open Preferences', to: '/account', color: 'neutral', variant: 'outline' }]"
          />
        </UCard>

        <UCard>
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <h2 class="font-semibold">Annotation types</h2>
              <UBadge
                v-if="!canManageSettings"
                label="Administrator"
                icon="i-lucide-lock"
                color="neutral"
                variant="subtle"
                size="sm"
              />
            </div>
            <p class="mt-1 text-sm text-muted">
              Names, colors and icons of the annotation types on every page, for everyone in this project. Each person
              can override them in Preferences on the account page.
            </p>
            <p v-if="!canManageSettings" class="mt-1 text-sm text-muted">
              Only Administrators change the project settings.
            </p>
          </template>
          <UAlert
            v-if="hasPersonalTypes"
            class="mb-4"
            color="neutral"
            variant="subtle"
            icon="i-lucide-user-cog"
            description="Your own Preferences override some of these for you."
            :actions="[{ label: 'Open Preferences', to: '/account', color: 'neutral', variant: 'outline' }]"
          />
          <TypeAppearanceEditor v-model="types" :inherited="builtInTypes" :disabled="!settingsEditable" />
          <template v-if="canManageSettings" #footer>
            <div class="flex justify-end">
              <UButton
                label="Reset to built-in"
                color="neutral"
                variant="ghost"
                size="sm"
                :disabled="!settingsEditable || Object.keys(types).length === 0"
                @click="types = {}"
              />
            </div>
          </template>
        </UCard>

        <div v-if="canManageSettings" class="space-y-3">
          <UAlert
            v-if="settingsConflict"
            color="warning"
            variant="subtle"
            icon="i-lucide-git-compare"
            title="Someone changed the project settings meanwhile; your changes were not saved"
            :description="
              settingsConflictRead || readingConflictSettings
                ? 'Load the new settings, or save yours over them.'
                : 'The new settings could not be read. Try again before you load them or save yours over them.'
            "
            :actions="[
              settingsConflictRead || readingConflictSettings
                ? {
                    label: 'Load the new settings',
                    color: 'neutral',
                    variant: 'outline',
                    disabled: !settingsConflictRead,
                    onClick: loadSavedSettings,
                  }
                : {
                    label: 'Read them again',
                    color: 'neutral',
                    variant: 'outline',
                    onClick: readConflictSettings,
                  },
            ]"
          />
          <UAlert
            v-if="settingsProblems"
            color="error"
            variant="subtle"
            icon="i-lucide-circle-alert"
            title="The project settings were not saved"
          >
            <template #description>
              <ul class="list-disc space-y-1 ps-4">
                <li v-for="(problem, index) in settingsProblems" :key="index">
                  <code v-if="problem.path" class="font-mono text-xs">{{ problem.path }}</code>
                  {{ problem.message }}
                </li>
              </ul>
            </template>
          </UAlert>
          <div class="flex flex-wrap items-center justify-end gap-2">
            <span v-if="settingsDirty" class="text-xs text-muted">Unsaved changes</span>
            <UButton
              label="Save project settings"
              :loading="savingSettings"
              :disabled="!settingsDirty"
              @click="saveSettings"
            />
          </div>
        </div>

        <UCard>
          <template #header>
            <div class="flex items-center justify-between">
              <h2 class="font-semibold">Machine authentication</h2>
              <UBadge
                v-if="!canAdmin"
                label="Administrator"
                icon="i-lucide-lock"
                color="neutral"
                variant="subtle"
                size="sm"
              />
            </div>
          </template>
          <p v-if="!canAdmin" class="text-sm text-muted">The policy is visible and editable for Administrators.</p>
          <LoadingState v-else-if="machinePolicy.isPending.value" />
          <ErrorState
            v-else-if="machinePolicy.isError.value"
            :error="machinePolicy.error.value"
            compact
            @retry="machinePolicy.refetch()"
          />
          <form v-else class="space-y-4" @submit.prevent="savePolicy">
            <UFormField
              label="Enabled credential kinds"
              help="Each new machine picks one of these."
              :error="enabledKinds.length === 0 ? 'Enable at least one kind.' : undefined"
            >
              <UCheckboxGroup v-model="enabledKinds" :items="kindItems" />
            </UFormField>
            <UFormField label="Default for new machines" help="Preselected when a machine is created.">
              <USelect
                v-model="defaultKind"
                :items="defaultKindItems"
                :disabled="defaultKindItems.length === 0"
                class="w-full max-w-sm"
              />
            </UFormField>
            <UFormField
              v-if="anyCertificates"
              label="Default certificate lifetime (days)"
              help="Preselected when a machine with a certificate is created; renewals keep the machine's lifetime."
            >
              <UInputNumber v-model="lifetime" :min="1" :max="3650" class="w-40" />
            </UFormField>
            <p class="text-xs text-muted">
              Machines keep the kind they were created with. Disabling a kind stops every machine that uses it until the
              kind is enabled again.
            </p>
            <div class="flex justify-end">
              <UButton
                type="submit"
                label="Save machine policy"
                :loading="saving"
                :disabled="enabledKinds.length === 0"
              />
            </div>
          </form>
        </UCard>

        <UCard>
          <template #header><h2 class="font-semibold">Certificate authority</h2></template>
          <p class="mb-3 text-sm text-muted">
            The CA that signs machine and shared certificates; servers that verify mTLS clients trust it.
          </p>
          <UButton
            label="Download CA certificate (PEM)"
            icon="i-lucide-download"
            color="neutral"
            variant="outline"
            @click="downloadCa"
          />
        </UCard>

        <section id="secrets" tabindex="-1" class="scroll-mt-4 space-y-6 outline-none">
          <div class="space-y-1">
            <h2 class="text-xs font-semibold text-muted uppercase">Secrets</h2>
            <p class="text-sm text-muted">
              How the key values of the project are protected. Everyone in the project sees these settings;
              Administrators change them.
            </p>
          </div>
          <LoadingState v-if="secretsPolicy.isPending.value" />
          <ErrorState
            v-else-if="secretsPolicy.isError.value"
            :error="secretsPolicy.error.value"
            compact
            @retry="secretsPolicy.refetch()"
          />
          <template v-else-if="secretsPolicy.data.value">
            <SecretsPolicyCard :policy="secretsPolicy.data.value" :editable="canAdmin" />
            <SecretsKeysCard :policy="secretsPolicy.data.value" :editable="canAdmin" />
            <ByokCard :policy="secretsPolicy.data.value" :editable="canAdmin" />
            <RecipientsCard :recipients="recipients" :editable="canAdmin" />
          </template>
        </section>
      </div>
    </template>
  </UDashboardPanel>
</template>
