<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import {
  getKeyValue,
  hasErrorCode,
  isPreconditionFailed,
  protectKeyValues,
  restoreKeyValues,
  setKeyValue,
  type KeyValueProtection,
} from '@42for.net/2splatform.sdk'
import { encryptForRecipients, getHyokPlace } from '@42for.net/2splatform.sdk/hyok'
import { useRevealedValue } from '@/composables/useRevealedValue'
import { invalidateKeyValues } from '@/queries/invalidation'
import { useKeyValue, useKeyValuePolicy, useRecipients } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'
import {
  bindingFor,
  daysUntil,
  isClientEncrypted,
  isSecretLevel,
  levelMeta,
  levels,
  maskedValue,
  serverSideLevels,
} from '@/utils/key-values'

/**
 * The Value tab of a pair (spec 29.3): metadata, the binding to copy, the public value in an editor, a masked field
 * with Reveal for a server-side secret, the recipients of a HYOK value, and Set new value. Saves send ExpectedVersion.
 */
const props = defineProps<{ keyName: string }>()

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const confirm = useConfirm()
const canEdit = useCan('edit')
const canSecrets = useCan('secrets')

const pair = useKeyValue(() => props.keyName)
const policy = useKeyValuePolicy()
const recipients = useRecipients()

const level = computed(() => pair.data.value?.Protection as KeyValueProtection | undefined)
const secret = computed(() => isSecretLevel(level.value))
const clientEncrypted = computed(() => isClientEncrypted(level.value))
const deleted = computed(() => !!pair.data.value?.IsDeleted)
const canWrite = computed(() => (secret.value ? canSecrets.value : canEdit.value) && !deleted.value)
const isJson = computed(() => pair.data.value?.ContentType === 'application/json')

// The binding to copy: the default source follows the level, the explicit sources are offered too.
const bindingSource = ref<'default' | 'public' | 'secret'>('default')
const binding = computed(() => bindingFor(props.keyName, bindingSource.value))

// Public values are edited in place; secrets get a new value through a separate field.
const draft = ref<string>()
const original = ref<string>()
const newValue = ref('')
const newLevel = ref<KeyValueProtection>()
const settingNew = ref(false)
const saving = ref(false)
const conflict = ref<{ version?: number; author?: string }>()
const saveError = ref<string>()

watch(
  () => pair.data.value,
  (value) => {
    if (value && !secret.value && draft.value === original.value) {
      original.value = value.Value ?? ''
      draft.value = value.Value ?? ''
    }
    if (value) {
      newLevel.value = value.Protection as KeyValueProtection
    }
  },
  { immediate: true },
)

const dirty = computed(() => (settingNew.value ? newValue.value.length > 0 : draft.value !== original.value))
useUnsavedChanges(dirty, { ignore: (to, from) => (to.params as { key?: string }).key !== (from.params as { key?: string }).key })

const revealed = useRevealedValue(async () => {
  if (!context.scope) {
    return undefined
  }
  const { org, project, view } = context.scope
  const fresh = await getKeyValue(org, project, view, props.keyName, { reveal: true, includeDeleted: true })
  return fresh.Value ?? undefined
})

const allowedLevels = computed(
  () => (policy.data.value?.AllowedProtections ?? ['Public', 'Standard']) as KeyValueProtection[],
)
const levelItems = computed(() =>
  levels
    .filter((entry) => allowedLevels.value.includes(entry))
    .map((entry) => ({ label: levelMeta[entry].label, value: entry })),
)
const enabledRecipients = computed(() => (recipients.data.value ?? []).filter((recipient) => recipient.IsEnabled !== false))

async function refresh() {
  if (context.scope) {
    await invalidateKeyValues(queryClient, context.scope)
  }
}

async function save(options: { overwrite?: boolean } = {}) {
  if (!context.scope || saving.value) {
    return
  }
  const { org, project, view } = context.scope
  const targetLevel = settingNew.value ? (newLevel.value ?? level.value) : level.value
  let value = settingNew.value ? newValue.value : (draft.value ?? '')

  saving.value = true
  saveError.value = undefined
  conflict.value = undefined

  try {
    if (isClientEncrypted(targetLevel)) {
      if (enabledRecipients.value.length === 0) {
        saveError.value = 'The project has no enabled recipient; add one in the Secrets settings.'
        return
      }
      value = await encryptForRecipients(
        value,
        enabledRecipients.value,
        getHyokPlace(org, project, view, props.keyName),
        pair.data.value?.ContentType || 'text/plain',
      )
    }

    const saved = await setKeyValue(org, project, view, props.keyName, {
      Protection: targetLevel !== level.value ? targetLevel : undefined,
      Value: value,
      ContentType: pair.data.value?.ContentType ?? undefined,
      ExpectedVersion: options.overwrite ? undefined : pair.data.value?.Version,
    })

    original.value = settingNew.value ? original.value : draft.value
    settingNew.value = false
    newValue.value = ''
    revealed.hide()
    await refresh()
    toast.add({ title: `Saved as version ${saved.Version ?? '?'}`, color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    if (isPreconditionFailed(error) || hasErrorCode(error, 'KeyValueVersionMismatch')) {
      const fresh = await pair.refetch()
      conflict.value = { version: fresh.data?.Version, author: fresh.data?.Author }
    } else if (hasErrorCode(error, 'ProtectionNotAllowed')) {
      saveError.value = `${error.message} An Administrator allows levels in the Secrets settings.`
    } else if (hasErrorCode(error, 'CustomerKeyUnavailable')) {
      saveError.value = `${error.message} The project's customer key (BYOK) is disabled, deleted or unreachable; see the Secrets settings.`
    } else {
      saveError.value = errorMessage(error)
    }
  } finally {
    saving.value = false
  }
}

function reload() {
  conflict.value = undefined
  original.value = pair.data.value?.Value ?? ''
  draft.value = original.value
}

async function changeProtection(target: KeyValueProtection) {
  if (!context.scope) {
    return
  }
  const confirmed = await confirm({
    title: `Move '${props.keyName}' to ${levelMeta[target].label}?`,
    description: 'The value is encrypted again under the new level as a new version. Older versions keep their previous level until they expire.',
    confirmLabel: 'Change protection',
    color: 'primary',
  })
  if (!confirmed) {
    return
  }
  try {
    const result = await protectKeyValues(context.scope.org, context.scope.project, context.scope.view, {
      Keys: [props.keyName],
      Protection: target,
    })
    const item = result.Items?.[0]
    if (item?.Outcome === 'Done' || item?.Outcome === 'Unchanged') {
      toast.add({ title: `'${props.keyName}' is ${levelMeta[target].label} now`, color: 'success', icon: 'i-lucide-check' })
    } else {
      toast.add({ title: 'The level was not changed', description: item?.Message ?? item?.Outcome, color: 'warning' })
    }
    await refresh()
  } catch (error) {
    toast.add({ title: 'The level was not changed', description: errorMessage(error), color: 'error' })
  }
}

async function restore() {
  if (!context.scope) {
    return
  }
  try {
    const result = await restoreKeyValues(context.scope.org, context.scope.project, context.scope.view, { Keys: [props.keyName] })
    const item = result.Items?.[0]
    if (item?.Outcome === 'Done') {
      toast.add({ title: `'${props.keyName}' restored`, color: 'success', icon: 'i-lucide-undo-2' })
    } else {
      toast.add({ title: 'The key was not restored', description: item?.Message ?? item?.Outcome, color: 'warning' })
    }
    await refresh()
  } catch (error) {
    toast.add({ title: 'The key was not restored', description: errorMessage(error), color: 'error' })
  }
}

const expiresIn = computed(() => daysUntil(pair.data.value?.ExpiresAt))
const protectTargets = computed(() =>
  serverSideLevels
    .filter((entry) => entry !== level.value && allowedLevels.value.includes(entry))
    .map((entry) => ({ label: `Move to ${levelMeta[entry].label}`, icon: levelMeta[entry].icon, onSelect: () => changeProtection(entry) })),
)
</script>

<template>
  <div class="space-y-4 p-4">
    <LoadingState v-if="pair.isPending.value" />
    <ErrorState v-else-if="pair.isError.value" :error="pair.error.value" compact @retry="pair.refetch()" />
    <template v-else-if="pair.data.value">
      <UAlert
        v-if="deleted"
        color="warning"
        variant="subtle"
        icon="i-lucide-trash-2"
        title="This key is deleted"
        :description="`Removed for good in ${expiresIn ?? '?'} day(s), with every version. Restore it to bring back the latest version.`"
        :actions="canWrite || (secret ? canSecrets : canEdit) ? [{ label: 'Restore', icon: 'i-lucide-undo-2', color: 'warning', onClick: restore }] : []"
      />

      <dl class="grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 gap-y-1.5 text-sm">
        <dt class="text-muted">Level</dt>
        <dd class="flex flex-wrap items-center gap-2">
          <KeyValueLevelBadge :level="level" :reference="pair.data.value.IsReference" />
          <UDropdownMenu v-if="canSecrets && !deleted && isSecretLevel(level) && !clientEncrypted && protectTargets.length" :items="protectTargets">
            <UButton label="Change protection" icon="i-lucide-arrow-right-left" color="neutral" variant="ghost" size="xs" />
          </UDropdownMenu>
        </dd>
        <dt class="text-muted">Version</dt>
        <dd class="font-mono text-xs">v{{ pair.data.value.Version }}</dd>
        <dt class="text-muted">Author</dt>
        <dd>{{ pair.data.value.Author }}</dd>
        <dt class="text-muted">Updated</dt>
        <dd>{{ pair.data.value.UpdatedAt ? formatDateTime(new Date(pair.data.value.UpdatedAt)) : '' }}</dd>
        <dt class="text-muted">Created</dt>
        <dd>{{ pair.data.value.CreatedBy }}, {{ pair.data.value.CreatedAt ? formatDateTime(new Date(pair.data.value.CreatedAt)) : '' }}</dd>
        <dt class="text-muted">Content type</dt>
        <dd class="font-mono text-xs">{{ pair.data.value.ContentType ?? 'text/plain' }}</dd>
        <dt class="text-muted">Binding</dt>
        <dd class="flex flex-wrap items-center gap-2">
          <USelect
            v-model="bindingSource"
            :items="[
              { label: 'default source', value: 'default' },
              { label: 'public only', value: 'public' },
              { label: 'secret only', value: 'secret' },
            ]"
            size="xs"
            class="w-36"
            aria-label="Binding source"
          />
          <code class="font-mono text-xs">{{ binding }}</code>
          <CopyButton :value="binding" />
        </dd>
      </dl>

      <!-- public value -->
      <template v-if="!secret">
        <UFormField label="Value" :help="pair.data.value.IsReference ? 'A reference to a secret in your own Key Vault; the application resolves it with its own identity.' : undefined">
          <CodeEditor v-if="isJson && draft !== undefined" v-model="draft" language="json" :readonly="!canWrite" @save="save()" />
          <UTextarea
            v-else
            v-model="draft"
            class="w-full font-mono"
            :rows="6"
            :disabled="!canWrite"
            autocomplete="off"
            @keydown.ctrl.s.prevent="save()"
            @keydown.meta.s.prevent="save()"
          />
        </UFormField>
      </template>

      <!-- server-side secret -->
      <template v-else-if="!clientEncrypted">
        <UFormField label="Value">
          <div class="flex flex-wrap items-center gap-2">
            <code class="min-w-0 flex-1 truncate rounded-md border border-default bg-elevated/50 px-3 py-2 font-mono text-sm" :title="revealed.value.value ? undefined : 'Masked'">
              {{ revealed.value.value ?? maskedValue }}
            </code>
            <template v-if="canSecrets && !deleted">
              <UButton
                v-if="!revealed.value.value"
                label="Reveal"
                icon="i-lucide-eye"
                color="neutral"
                variant="outline"
                size="sm"
                :loading="revealed.loading.value"
                @click="revealed.reveal()"
              />
              <UButton v-else label="Hide" icon="i-lucide-eye-off" color="neutral" variant="outline" size="sm" @click="revealed.hide()" />
              <CopyButton v-if="revealed.value.value" :value="revealed.value.value" secret size="sm" />
            </template>
          </div>
          <template #help>
            <span v-if="revealed.value.value">Masked again after 60 seconds.</span>
            <span v-else-if="!canSecrets">Secret values are shown to the ContributorWithSecrets role and above.</span>
            <span v-else-if="revealed.error.value" class="text-error">{{ errorMessage(revealed.error.value) }}</span>
          </template>
        </UFormField>
      </template>

      <!-- client-encrypted -->
      <template v-else>
        <UAlert
          color="secondary"
          variant="subtle"
          icon="i-lucide-eye-off"
          title="Encrypted on the client"
          :description="`The platform stores a document encrypted for ${pair.data.value.Recipients?.length ?? 0} recipient(s) and cannot read it. Only the holder of a recipient's private key decrypts it.`"
        />
        <div v-if="pair.data.value.Recipients?.length" class="flex flex-wrap gap-1">
          <UBadge v-for="recipient in pair.data.value.Recipients" :key="recipient" :label="recipient" icon="i-lucide-user-round" color="neutral" variant="subtle" size="sm" />
        </div>
      </template>

      <!-- a new value for a secret -->
      <div v-if="secret && canSecrets && !deleted" class="space-y-3">
        <UButton v-if="!settingNew" label="Set new value" icon="i-lucide-pencil" color="neutral" variant="outline" size="sm" @click="settingNew = true" />
        <template v-else>
          <UFormField label="New level">
            <USelect v-model="newLevel" :items="levelItems" class="w-48" aria-label="New protection level" />
            <template #help>
              <span v-if="newLevel && newLevel !== level">
                Older versions keep their {{ levelMeta[level ?? 'Public'].label }} level until they expire.
                <span v-if="newLevel === 'Public'">The secret becomes public for every reader.</span>
              </span>
              <span v-else-if="clientEncrypted">Encrypted in your browser for {{ enabledRecipients.length }} recipient(s).</span>
            </template>
          </UFormField>
          <UFormField label="New value">
            <UTextarea v-model="newValue" class="w-full font-mono" :rows="3" autocomplete="off" @keydown.ctrl.s.prevent="save()" @keydown.meta.s.prevent="save()" />
          </UFormField>
        </template>
      </div>

      <UAlert
        v-if="conflict"
        color="warning"
        variant="subtle"
        icon="i-lucide-git-compare"
        title="Someone else changed this key"
        :description="`It is at version ${conflict.version ?? '?'} now (${conflict.author ?? 'unknown author'}). Reload to see it, or overwrite with your value.`"
        :actions="[
          { label: 'Reload', color: 'neutral', variant: 'outline', onClick: reload },
          { label: 'Overwrite', color: 'warning', onClick: () => save({ overwrite: true }) },
        ]"
      />
      <UAlert v-if="saveError" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="saveError" />

      <div v-if="canWrite" class="flex flex-wrap items-center justify-end gap-2">
        <span v-if="dirty" class="text-xs text-muted">Unsaved changes</span>
        <UButton v-if="settingNew" label="Cancel" color="neutral" variant="ghost" @click="((settingNew = false), (newValue = ''))" />
        <UButton label="Save" icon="i-lucide-save" :disabled="!dirty" :loading="saving" @click="save()" />
      </div>
    </template>
  </div>
</template>
