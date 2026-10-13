<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useForm } from 'vee-validate'
import { hasErrorCode, isConflict, setKeyValue, type KeyValue, type KeyValueProtection } from '@42for.net/2splatform.sdk'
import { encryptForRecipients, getHyokPlace } from '@42for.net/2splatform.sdk/hyok'
import { keyValueFormSchema, type KeyValueForm } from '@/forms/key-value.schema'
import { toTypedSchema } from '@/forms/typed-schema'
import { useKeyValuePolicy, useRecipients } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { contentTypes, isClientEncrypted, isSecretLevel, levelMeta, levels, secretReferenceContentType } from '@/utils/key-values'

/**
 * New pair (spec 29.3): key, level (from the policy's allowed levels), value and content type. A HYOK value is
 * encrypted in the browser for the enabled recipients before it leaves; the plaintext is never sent.
 */
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ created: [pair: KeyValue] }>()

const context = useContextStore()
const policy = useKeyValuePolicy()
const recipients = useRecipients()
const canSecrets = useCan('secrets')

const allowed = computed<KeyValueProtection[]>(() => {
  const fromPolicy = (policy.data.value?.AllowedProtections ?? ['Public', 'Standard']) as KeyValueProtection[]
  return levels.filter((level) => fromPolicy.includes(level) && (level === 'Public' || canSecrets.value))
})
const levelItems = computed(() =>
  allowed.value.map((level) => ({ label: levelMeta[level].label, value: level, description: levelMeta[level].description })),
)

const { defineField, errors, handleSubmit, isSubmitting, resetForm, setFieldError, values } = useForm({
  validationSchema: toTypedSchema(keyValueFormSchema),
  initialValues: { key: '', protection: 'Public', value: '', contentType: 'text/plain' } satisfies KeyValueForm,
})
const [key, keyAttrs] = defineField('key')
const [protection] = defineField('protection')
const [value, valueAttrs] = defineField('value')
const [contentType] = defineField('contentType')
const formError = ref<string>()
const secretDefault = ref(false)

watch(open, (isOpen) => {
  if (isOpen) {
    resetForm()
    secretDefault.value = false
    formError.value = undefined
  }
})

// "Secret" picks the project's default secret level; the level select narrows it further.
watch(secretDefault, (secret) => {
  const fallback = (policy.data.value?.DefaultSecretProtection ?? 'Standard') as KeyValueProtection
  protection.value = secret ? (allowed.value.includes(fallback) ? fallback : (allowed.value.find(isSecretLevel) ?? 'Public')) : 'Public'
})

const isReference = computed(() => values.contentType === secretReferenceContentType)
const hyokRecipients = computed(() => (recipients.data.value ?? []).filter((recipient) => recipient.IsEnabled !== false))

const onSubmit = handleSubmit(async (form) => {
  formError.value = undefined
  if (!context.scope) {
    return
  }

  if (isReference.value && form.protection !== 'Public') {
    setFieldError('contentType', 'A reference is a public pair; the platform never resolves it.')
    return
  }

  let sent = form.value
  if (isClientEncrypted(form.protection)) {
    if (hyokRecipients.value.length === 0) {
      setFieldError('protection', 'The project has no enabled recipient; add one in the Secrets settings.')
      return
    }
    const { org, project, view } = context.scope
    sent = await encryptForRecipients(form.value, hyokRecipients.value, getHyokPlace(org, project, view, form.key), form.contentType || 'text/plain')
  }

  try {
    const pair = await setKeyValue(context.scope.org, context.scope.project, context.scope.view, form.key, {
      Protection: form.protection as KeyValueProtection,
      Value: sent,
      ContentType: form.contentType || undefined,
      ExpectedVersion: 0,
    })
    open.value = false
    emit('created', pair)
  } catch (error) {
    if (hasErrorCode(error, 'KeyValueVersionMismatch')) {
      setFieldError('key', 'This key exists already; open it to change its value.')
    } else if (hasErrorCode(error, 'ProtectionNotAllowed')) {
      setFieldError('protection', `${error.message} Allow it in the Secrets settings.`)
    } else if (hasErrorCode(error, 'InvalidSecretReference') || hasErrorCode(error, 'InvalidKey') || hasErrorCode(error, 'ValueTooLarge')) {
      setFieldError(hasErrorCode(error, 'InvalidKey') ? 'key' : 'value', error.message)
    } else if (isConflict(error)) {
      formError.value = errorMessage(error)
    } else {
      formError.value = errorMessage(error)
    }
  }
})
</script>

<template>
  <UModal v-model:open="open" title="New key" :description="`In ${context.org}/${context.project}, view ${context.view}.`">
    <template #body>
      <form id="key-value-create" class="space-y-4" novalidate @submit="onSubmit">
        <UFormField label="Key" name="key" required :error="errors.key" help="Dot-separated, for example db.password.">
          <UInput v-model="key" v-bind="keyAttrs" class="w-full font-mono" autocomplete="off" autofocus placeholder="db.password" />
        </UFormField>

        <div class="flex flex-wrap items-end gap-3">
          <USwitch v-model="secretDefault" label="Secret" description="The project's default secret level." />
        </div>
        <UFormField label="Level" name="protection" :error="errors.protection">
          <USelect v-model="protection" :items="levelItems" class="w-48" aria-label="Protection level" />
          <template #help>
            {{ levelMeta[(protection ?? 'Public') as KeyValueProtection]?.description }}
            <span v-if="isClientEncrypted(protection)">
              Encrypted in your browser for {{ hyokRecipients.length }} recipient(s).
            </span>
          </template>
        </UFormField>

        <UFormField label="Content type" name="contentType" :error="errors.contentType">
          <USelect v-model="contentType" :items="[...contentTypes]" class="w-full max-w-xs" />
        </UFormField>

        <UFormField label="Value" name="value" :error="errors.value" :help="isReference ? 'kv://<vault>/<name> or https://<vault>.vault.azure.net/secrets/<name>' : undefined">
          <UTextarea
            v-model="value"
            v-bind="valueAttrs"
            class="w-full font-mono"
            :rows="isSecretLevel(protection) ? 2 : 4"
            autocomplete="off"
            :placeholder="isReference ? 'kv://my-vault/db-password' : ''"
          />
        </UFormField>

        <UAlert v-if="formError" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="formError" />
      </form>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
        <UButton type="submit" form="key-value-create" label="Create" icon="i-lucide-plus" :loading="isSubmitting" />
      </div>
    </template>
  </UModal>
</template>
