<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import {
  createMachineAccess,
  type MachineAccess,
  type MachineAuthenticationPolicy,
  type MachineCredentialKind,
  type MachineScope,
} from '@42for.net/2splatform.sdk'
import { useGetFederatedIssuers } from '@42for.net/2splatform.sdk/vue-query'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { isRuleComplete, projectTokenEndpoint, ruleDraft, toFederationRule, type RuleDraft } from '@/utils/federation'
import {
  apiBaseUrl,
  credentialKindDescriptions,
  credentialKinds,
  impliedReads,
  inApiOrder,
  isFederated,
  isManagementScope,
  isSecretsScope,
  issuesCertificates,
  managementScopes,
  requiredRoleFor,
  runtimeScopes,
  scopeDescriptions,
  scopeLabels,
  scopePresets,
  withImpliedReads,
  type ScopePreset,
} from '@/utils/machines'

/**
 * New machine access (spec section 13): its scopes, an optional annotation restriction, the credential kind (one of
 * the kinds the project enables, 424-14) and the certificate lifetime. The project policy preselects the kind and the
 * lifetime; without it (still loading) the server applies the project defaults. A CertificateAndApiKey machine can use
 * a shared certificate of the project instead of its own: then no certificate (and no lifetime) is issued for it.
 * A Federated machine (424-46) gets federation rules instead: a trusted issuer of the project, the audience (prefilled
 * with the project's token endpoint at the address the UI calls the API at; the API may name it differently behind a
 * proxy, and the machine detail flags such a rule), the subject and claim pins; Create waits until every rule is
 * complete. Without a trusted issuer, Administrators are sent to the Trusted issuers section and Contributors are told
 * to ask one. The scopes (424-79) come in two groups: runtime scopes, of which Create needs one, and management scopes
 * (424-57), bulk and structural operations. A preset replaces the picked scopes and is never sent; every open starts
 * from Runtime reader. A write scope comes with its read scope, as the API stores it. As in the API, management scopes
 * are disabled below Administrator and secrets scopes below ContributorWithSecrets, and so are the presets with one.
 */
const props = defineProps<{ policy?: MachineAuthenticationPolicy }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [machine: MachineAccess] }>()

const context = useContextStore()
const catalog = useCatalogStore()
const role = useRole()
const canAdmin = useCan('manageAccess')
const canGiveSecrets = useCan('secrets')

const runtime = ref<MachineScope[]>([])
const management = ref<MachineScope[]>([])
const annotationKey = ref('')
const kind = ref<MachineCredentialKind>()
const lifetime = ref<number>(365)
const useSharedCertificate = ref(false)
const rules = ref<RuleDraft[]>([])
const saving = ref(false)
const failure = ref<string>()
/** Set while the dialog closes through 'Add a trusted issuer' (see closeAutoFocus). */
let followingIssuersLink = false

const kindItems = computed(() =>
  credentialKinds.filter((value) => props.policy?.EnabledCredentialKinds?.includes(value)),
)

const federated = computed(() => isFederated(kind.value))
// Contributors read the trusted issuers to write rules; only Administrators add them.
const issuers = useGetFederatedIssuers(
  () => context.org ?? '',
  () => context.project ?? '',
  { query: { enabled: () => open.value && federated.value && !!context.project, meta: { silent: true } } },
)
const tokenEndpoint = computed(() => projectTokenEndpoint(apiBaseUrl(), context.org ?? '', context.project ?? ''))

watch(open, (value) => {
  if (value) {
    failure.value = undefined
    followingIssuersLink = false
    // Every open starts from Runtime reader: a grant of the previous machine must not carry over to the next one.
    applyPreset(scopePresets[0]!)
    kind.value = props.policy?.CredentialKind ?? kindItems.value[0]
    lifetime.value = props.policy?.CertificateLifetimeDays ?? 365
    useSharedCertificate.value = false
    rules.value = [ruleDraft(undefined, tokenEndpoint.value)]
    preselectIssuer()
    if (context.org && context.project) {
      void catalog.load({ org: context.org, project: context.project, view: 'default' })
    }
  }
})

/** With a single trusted issuer, a rule that names none takes it. */
function preselectIssuer() {
  const list = issuers.data.value
  if (list?.length === 1) {
    rules.value = rules.value.map((rule) => (rule.issuer ? rule : { ...rule, issuer: list[0]!.Name ?? '' }))
  }
}

watch(() => issuers.data.value, preselectIssuer)

// The policy may arrive, or change, after the dialog opened: preselect its kind when none is picked yet (the lifetime
// field shows only once a kind is picked, so it has not been edited either), and replace a picked kind the policy no
// longer enables. A picked kind that stays enabled is kept.
watch(
  () => props.policy,
  (policy) => {
    if (!open.value || !policy) {
      return
    }
    if (!kind.value) {
      kind.value = policy.CredentialKind ?? kindItems.value[0]
      lifetime.value = policy.CertificateLifetimeDays ?? lifetime.value
    } else if (!kindItems.value.includes(kind.value)) {
      kind.value = policy.CredentialKind ?? kindItems.value[0]
    }
  },
)

const sharedCertificate = computed(() => kind.value === 'CertificateAndApiKey' && useSharedCertificate.value)
const ownCertificate = computed(() => issuesCertificates(kind.value) && !sharedCertificate.value)
const noIssuers = computed(() => federated.value && issuers.isSuccess.value && !issuers.data.value?.length)
const canCreate = computed(
  () =>
    runtime.value.length > 0 &&
    (!federated.value || (!noIssuers.value && rules.value.length > 0 && rules.value.every(isRuleComplete))),
)
const trustedIssuers = computed(() => ({
  path: `/${context.org}/${context.project}/machines`,
  hash: '#trusted-issuers',
}))

// 'Add a trusted issuer' closes the dialog while its link changes the hash, and useHashScroll focuses the section. The
// dialog gives the focus back to its trigger (New machine) only once its leave transition ends, so for this exit it
// keeps the focus on the section instead; Cancel and Escape restore it as usual.
function followIssuersLink() {
  followingIssuersLink = true
  open.value = false
}

function closeAutoFocus(event: Event) {
  if (!followingIssuersLink) {
    return
  }
  followingIssuersLink = false
  event.preventDefault()
  // The hash scroll has usually focused the section; not yet without a leave transition, and not at all when the hash
  // named the section already (then the route does not change).
  const section = document.getElementById(trustedIssuers.value.hash.slice(1))
  if (section && document.activeElement !== section) {
    section.scrollIntoView({ block: 'start' })
    section.focus({ preventScroll: true })
  }
}

const scopeItem = (value: MachineScope) => ({ label: scopeLabels[value], value, description: scopeDescriptions[value] })
const runtimeItems = computed(() =>
  runtimeScopes.map((value) => ({ ...scopeItem(value), disabled: isSecretsScope(value) && !canGiveSecrets.value })),
)
const managementItems = managementScopes.map(scopeItem)
const presetsId = useId()
const keys = computed(() => [...catalog.byKey.keys()].toSorted())

/** Whether the user may give every scope of the preset (the API refuses the machine otherwise). */
const canApply = (preset: ScopePreset) => role.can(requiredRoleFor(preset.scopes))

function applyPreset(preset: ScopePreset) {
  runtime.value = preset.scopes.filter((scope) => !isManagementScope(scope))
  management.value = preset.scopes.filter(isManagementScope)
}

// Picking X.Write picks X.Read as well, and dropping X.Read drops X.Write: the pick is what the API stores.
function pickRuntime(next: MachineScope[]) {
  const dropped = runtime.value.filter((scope) => !next.includes(scope))
  runtime.value = withImpliedReads(next.filter((scope) => !dropped.some((read) => impliedReads[scope] === read)))
}

async function create() {
  saving.value = true
  failure.value = undefined
  try {
    const machine = await createMachineAccess(context.org!, context.project!, {
      Organization: context.org,
      Project: context.project,
      Scopes: inApiOrder([...runtime.value, ...management.value]),
      AnnotationKey: annotationKey.value.trim() || undefined,
      CredentialKind: kind.value,
      CertificateLifetimeDays: ownCertificate.value ? lifetime.value : null,
      UseSharedCertificate: sharedCertificate.value || undefined,
      FederationRules: federated.value ? rules.value.map(toFederationRule) : undefined,
    })
    open.value = false
    annotationKey.value = ''
    emit('created', machine)
  } catch (error) {
    failure.value = errorMessage(error)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    class="ph-no-capture"
    title="New machine access"
    :description="`For ${context.org}/${context.project}.`"
    :ui="{ content: federated ? 'max-w-2xl' : undefined }"
    :content="{ onCloseAutoFocus: closeAutoFocus }"
  >
    <template #body>
      <div class="space-y-4">
        <div role="group" :aria-labelledby="`${presetsId}-label`" class="space-y-2">
          <div>
            <p :id="`${presetsId}-label`" class="text-sm font-medium text-default">Presets</p>
            <p class="text-sm text-muted">Replace the scopes below with those of a typical machine.</p>
          </div>
          <div class="flex flex-wrap gap-1">
            <UButton
              v-for="preset in scopePresets"
              :key="preset.name"
              :label="preset.name"
              size="xs"
              color="neutral"
              variant="outline"
              :disabled="!canApply(preset)"
              @click="applyPreset(preset)"
            />
          </div>
        </div>
        <UFormField
          label="Runtime scopes"
          description="Reads and writes of single documents. A write scope comes with its read scope."
          :help="canGiveSecrets ? undefined : 'Only users with secret rights give secrets scopes.'"
          :error="runtime.length === 0 ? 'Pick at least one runtime scope.' : undefined"
          required
        >
          <UCheckboxGroup :model-value="runtime" :items="runtimeItems" @update:model-value="pickRuntime" />
        </UFormField>
        <UFormField
          label="Management scopes"
          description="Bulk and structural operations. Each also needs the runtime scope of its operation; give them only to machines that need them."
          :help="canAdmin ? undefined : 'Only Administrators give management scopes.'"
          :ui="{ description: 'text-warning' }"
        >
          <UCheckboxGroup v-model="management" :items="managementItems" :disabled="!canAdmin" />
        </UFormField>
        <UFormField label="Annotation key" help="Optional: limits the machine to this annotation and its descendants.">
          <UInputMenu
            v-model="annotationKey"
            :items="keys"
            class="w-full"
            :ui="{ base: 'font-mono' }"
            placeholder="Any annotation"
            virtualize
          />
        </UFormField>
        <UFormField v-if="kind" label="Credential kind" :help="credentialKindDescriptions[kind]">
          <USelect v-model="kind" :items="kindItems" :disabled="kindItems.length < 2" class="w-full" />
        </UFormField>
        <UFormField
          v-if="kind === 'CertificateAndApiKey'"
          :help="
            useSharedCertificate
              ? 'No certificate is issued for this machine: it signs in with a shared certificate of the project and its API key.'
              : 'A certificate is issued for this machine.'
          "
        >
          <UCheckbox v-model="useSharedCertificate" label="Use a shared certificate of the project" />
        </UFormField>
        <UFormField v-if="ownCertificate" label="Certificate lifetime (days)">
          <UInputNumber v-model="lifetime" :min="1" :max="3650" class="w-40" />
        </UFormField>
        <template v-if="federated">
          <LoadingState v-if="issuers.isPending.value" label="Loading the trusted issuers…" />
          <ErrorState v-else-if="issuers.isError.value" :error="issuers.error.value" @retry="issuers.refetch()" />
          <UAlert
            v-else-if="noIssuers"
            color="warning"
            variant="subtle"
            icon="i-lucide-badge-check"
            title="No trusted issuers"
            :description="
              canAdmin
                ? 'A Federated machine exchanges tokens of a trusted issuer of the project. Add one first, for example for GitHub Actions or Kubernetes.'
                : 'A Federated machine exchanges tokens of a trusted issuer of the project, and the project has none yet. Ask an Administrator of the project to add one.'
            "
            :actions="
              canAdmin
                ? [
                    {
                      label: 'Add a trusted issuer',
                      to: trustedIssuers,
                      color: 'neutral',
                      variant: 'outline',
                      onClick: followIssuersLink,
                    },
                  ]
                : undefined
            "
          />
          <FederationRuleEditor v-else v-model="rules" :issuers="issuers.data.value ?? []" :audience="tokenEndpoint" />
        </template>
        <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton label="Create" icon="i-lucide-computer" :loading="saving" :disabled="!canCreate" @click="create" />
        </div>
      </div>
    </template>
  </UModal>
</template>
