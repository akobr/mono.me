<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  createMachineAccess,
  type MachineAccess,
  type MachineAccessCreateScope,
  type MachineAuthenticationPolicy,
  type MachineCredentialKind,
} from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { credentialKindDescriptions, credentialKinds, issuesCertificates, scopeDescriptions } from '@/utils/machines'

/**
 * New machine access (spec section 13): scope, an optional annotation restriction, the credential kind (one of the
 * kinds the project enables, 424-14) and the certificate lifetime. The project policy preselects the kind and the
 * lifetime; without it (still loading) the server applies the project defaults.
 */
const props = defineProps<{ policy?: MachineAuthenticationPolicy }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [machine: MachineAccess] }>()

const context = useContextStore()
const catalog = useCatalogStore()

const scope = ref<MachineAccessCreateScope>('DefaultRead')
const annotationKey = ref('')
const kind = ref<MachineCredentialKind>()
const lifetime = ref<number>(365)
const saving = ref(false)
const failure = ref<string>()

const kindItems = computed(() =>
  credentialKinds.filter((value) => props.policy?.EnabledCredentialKinds?.includes(value)),
)

watch(open, (value) => {
  if (value) {
    failure.value = undefined
    kind.value = props.policy?.CredentialKind ?? kindItems.value[0]
    lifetime.value = props.policy?.CertificateLifetimeDays ?? 365
    if (context.org && context.project) {
      void catalog.load({ org: context.org, project: context.project, view: 'default' })
    }
  }
})

const scopes = Object.entries(scopeDescriptions).map(([value, description]) => ({ label: value, value, description }))
const keys = computed(() => [...catalog.byKey.keys()].toSorted())

async function create() {
  saving.value = true
  failure.value = undefined
  try {
    const machine = await createMachineAccess(context.org!, context.project!, {
      Organization: context.org,
      Project: context.project,
      Scope: scope.value,
      AnnotationKey: annotationKey.value.trim() || undefined,
      CredentialKind: kind.value,
      CertificateLifetimeDays: issuesCertificates(kind.value) ? lifetime.value : null,
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
  <UModal v-model:open="open" title="New machine access" :description="`For ${context.org}/${context.project}.`">
    <template #body>
      <div class="space-y-4">
        <UFormField label="Scope" required>
          <USelect v-model="scope" :items="scopes" class="w-full" />
          <template #help>{{ scopeDescriptions[scope] }}</template>
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
        <UFormField v-if="issuesCertificates(kind)" label="Certificate lifetime (days)">
          <UInputNumber v-model="lifetime" :min="1" :max="825" class="w-40" />
        </UFormField>
        <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />
        <div class="flex justify-end gap-2">
          <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
          <UButton label="Create" icon="i-lucide-computer" :loading="saving" @click="create" />
        </div>
      </div>
    </template>
  </UModal>
</template>
