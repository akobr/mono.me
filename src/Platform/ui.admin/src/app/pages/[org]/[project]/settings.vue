<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getCertificateAuthority,
  setMachineAuthentication,
  type MachineAuthenticationPolicy,
} from '@42for.net/2splatform.sdk'
import { useGetAccessPoint } from '@42for.net/2splatform.sdk/vue-query'
import { invalidateAccess } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { dateFormatItems, type DateFormat } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'
import { credentialKindDescriptions, issuesCertificates } from '@/utils/machines'
import { resolveTypeAppearances, type TypeAppearances } from '@/utils/type-appearance'

// Project settings (spec section 13): the machine authentication policy (Administrator), the CA certificate
// download (PEM; the SDK fetcher returns non-JSON bodies as text) and project information. The default time zone of
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

const policy = ref<MachineAuthenticationPolicy>({ CredentialKind: 'ApiKey', CertificateLifetimeDays: 90 })
watch(
  () => accessPoint.data.value?.MachineAuthentication,
  (value) => value && (policy.value = { ...value }),
  { immediate: true },
)

const saving = ref(false)
const kinds = Object.entries(credentialKindDescriptions).map(([value]) => ({ label: value, value }))

async function savePolicy() {
  saving.value = true
  try {
    await setMachineAuthentication(context.projectKey!, {
      CredentialKind: policy.value.CredentialKind,
      CertificateLifetimeDays: issuesCertificates(policy.value) ? policy.value.CertificateLifetimeDays : null,
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
          <LoadingState v-else-if="accessPoint.isPending.value" />
          <ErrorState
            v-else-if="accessPoint.isError.value"
            :error="accessPoint.error.value"
            compact
            @retry="accessPoint.refetch()"
          />
          <form v-else class="space-y-4" @submit.prevent="savePolicy">
            <UFormField
              label="Credential kind"
              :help="policy.CredentialKind ? credentialKindDescriptions[policy.CredentialKind] : undefined"
            >
              <USelect v-model="policy.CredentialKind" :items="kinds" class="w-full max-w-sm" />
            </UFormField>
            <UFormField v-if="issuesCertificates(policy)" label="Certificate lifetime (days)">
              <UInputNumber v-model="policy.CertificateLifetimeDays" :min="1" :max="825" class="w-40" />
            </UFormField>
            <p class="text-xs text-muted">The policy applies to machines created or reset after the change.</p>
            <div class="flex justify-end">
              <UButton type="submit" label="Save policy" :loading="saving" />
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
