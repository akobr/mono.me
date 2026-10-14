<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getCertificateAuthority,
  setMachineAuthentication,
  type MachineCredentialKind,
} from '@42for.net/2splatform.sdk'
import { useGetAccessPoint, useGetMachineAuthentication } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { dateFormatItems, type DateFormat } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'
import { credentialKindDescriptions, credentialKinds, issuesCertificates } from '@/utils/machines'
import { resolveTypeAppearances, type TypeAppearances } from '@/utils/type-appearance'

// Project settings (spec section 13): the machine authentication policy (Administrator: the enabled credential
// kinds, the default kind and certificate lifetime of new machines, 424-14), the CA certificate download (PEM; the
// SDK fetcher returns non-JSON bodies as text) and project information. The default time zone of
// new annotations (424-12), the date format (424-23) and the names, colors and icons of the annotation types (424-13)
// are kept in this browser, so they apply at once, like Preferences.
const context = useContextStore()
const projectSettings = useProjectSettingsStore()
const preferences = usePreferencesStore()
const queryClient = useQueryClient()
const toast = useToast()
const role = useRole()
const canAdmin = useCan('manageAccess')
const canEdit = useCan('edit')

const defaultTimeZone = computed({
  get: () => projectSettings.defaultTimeZone,
  set: (value: string) => {
    if (context.projectKey) {
      projectSettings.update(context.projectKey, { defaultTimeZone: value })
    }
  },
})

const dateFormat = computed({
  get: () => projectSettings.dateFormat,
  set: (value: DateFormat) => {
    if (context.projectKey) {
      projectSettings.update(context.projectKey, { dateFormat: value })
    }
  },
})
const dateFormatOptions = dateFormatItems()

const types = computed({
  get: () => projectSettings.current.types,
  set: (value: TypeAppearances) => {
    if (context.projectKey) {
      projectSettings.update(context.projectKey, { types: value })
    }
  },
})
const builtInTypes = resolveTypeAppearances()
const hasPersonalTypes = computed(() => Object.keys(preferences.types).length > 0)

const accessPoint = useGetAccessPoint(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canAdmin.value, meta: { silent: true } },
})

// The policy as the API resolves it: every property is filled, deployment defaults included.
const machinePolicy = useGetMachineAuthentication(() => context.projectKey ?? '', {
  query: { enabled: () => !!context.projectKey && canAdmin.value, meta: { silent: true } },
})

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
        </UCard>

        <UCard>
          <template #header><h2 class="font-semibold">Annotations</h2></template>
          <UFormField
            label="Default time zone"
            help="New annotations in this project start in this time zone. Saved in this browser."
          >
            <TimeZoneSelect
              v-model="defaultTimeZone"
              :disabled="!canEdit"
              aria-label="Default time zone"
              class="max-w-sm"
            />
          </UFormField>
        </UCard>

        <UCard>
          <template #header><h2 class="font-semibold">Dates</h2></template>
          <UFormField
            label="Date format"
            help="How dates are shown on every page of this project, for everyone who opens it in this browser. Each person can pick their own format in Preferences."
          >
            <USelect v-model="dateFormat" :items="dateFormatOptions" :disabled="!canEdit" class="w-full max-w-sm" />
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
            <h2 class="font-semibold">Annotation types</h2>
            <p class="mt-1 text-sm text-muted">
              Names, colors and icons of the annotation types on every page, for everyone who opens this project in this
              browser. Each person can override them in Preferences on the account page.
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
          <TypeAppearanceEditor v-model="types" :inherited="builtInTypes" :disabled="!canEdit" />
          <template v-if="canEdit" #footer>
            <div class="flex justify-end">
              <UButton
                label="Reset to built-in"
                color="neutral"
                variant="ghost"
                size="sm"
                :disabled="Object.keys(types).length === 0"
                @click="types = {}"
              />
            </div>
          </template>
        </UCard>

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
              <UInputNumber v-model="lifetime" :min="1" :max="825" class="w-40" />
            </UFormField>
            <p class="text-xs text-muted">
              Machines keep the kind they were created with. Disabling a kind stops every machine that uses it until the
              kind is enabled again.
            </p>
            <div class="flex justify-end">
              <UButton type="submit" label="Save policy" :loading="saving" :disabled="enabledKinds.length === 0" />
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
      </div>
    </template>
  </UDashboardPanel>
</template>
