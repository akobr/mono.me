<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { hasErrorCode, setKeyValuePolicy, type KeyValuePolicy, type KeyValueProtection } from '@42for.net/2splatform.sdk'
import { invalidateKeyValuePolicy } from '@/queries/invalidation'
import { useContextStore } from '@/stores/context'
import { errorMessage } from '@/utils/errors'
import { isSecretLevel, levelMeta, levels, parseProtectionInUse, type ProtectionInUse } from '@/utils/key-values'

/**
 * Allowed levels, the default secret level and the mTLS rule (spec 29.6). Disabling a level that keys still carry
 * shows the counts of the 409 ProtectionInUse with links to the filtered lists.
 */
const props = defineProps<{ policy: KeyValuePolicy; editable: boolean }>()

const context = useContextStore()
const queryClient = useQueryClient()
const toast = useToast()
const confirm = useConfirm()

const allowed = ref<Set<KeyValueProtection>>(new Set())
const defaultLevel = ref<KeyValueProtection>('Standard')
const requireMtls = ref(true)
const saving = ref(false)
const inUse = ref<ProtectionInUse>()
const failure = ref<string>()

watch(
  () => props.policy,
  (policy) => {
    allowed.value = new Set((policy.AllowedProtections ?? ['Public', 'Standard']) as KeyValueProtection[])
    defaultLevel.value = (policy.DefaultSecretProtection ?? 'Standard') as KeyValueProtection
    requireMtls.value = policy.RequireMtlsForSecretDelivery ?? true
  },
  { immediate: true },
)

const dirty = computed(
  () =>
    [...allowed.value].toSorted().join() !== [...((props.policy.AllowedProtections ?? []) as string[])].toSorted().join() ||
    defaultLevel.value !== (props.policy.DefaultSecretProtection ?? 'Standard') ||
    requireMtls.value !== (props.policy.RequireMtlsForSecretDelivery ?? true),
)

const defaultItems = computed(() =>
  levels.filter((level) => isSecretLevel(level) && allowed.value.has(level)).map((level) => ({ label: levelMeta[level].label, value: level })),
)

function toggle(level: KeyValueProtection, on: boolean) {
  const next = new Set(allowed.value)
  if (on) {
    next.add(level)
  } else {
    next.delete(level)
  }
  allowed.value = next
}

async function save() {
  if (!context.org || !context.project) {
    return
  }
  if (!requireMtls.value && (props.policy.RequireMtlsForSecretDelivery ?? true)) {
    const confirmed = await confirm({
      title: 'Deliver secrets to machines without mTLS?',
      description: 'Machines with an API key or a bearer token alone will receive secret values when they hold the Configuration.Secrets scope.',
      confirmLabel: 'Turn off the mTLS rule',
      color: 'warning',
    })
    if (!confirmed) {
      requireMtls.value = true
      return
    }
  }

  saving.value = true
  inUse.value = undefined
  failure.value = undefined
  try {
    await setKeyValuePolicy(context.org, context.project, {
      AllowedProtections: levels.filter((level) => allowed.value.has(level)),
      DefaultSecretProtection: allowed.value.has(defaultLevel.value) ? defaultLevel.value : undefined,
      RequireMtlsForSecretDelivery: requireMtls.value,
      ExpectedVersion: props.policy.Version,
    })
    await invalidateKeyValuePolicy(queryClient, context.org, context.project)
    toast.add({ title: 'Secrets policy saved', color: 'success', icon: 'i-lucide-check' })
  } catch (error) {
    inUse.value = parseProtectionInUse(error)
    if (!inUse.value) {
      failure.value = hasErrorCode(error, 'PolicyVersionMismatch')
        ? 'The policy changed meanwhile; reload the page and try again.'
        : errorMessage(error)
    }
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-center justify-between">
        <h3 class="font-semibold">Allowed levels</h3>
        <UBadge v-if="!editable" label="Administrator" icon="i-lucide-lock" color="neutral" variant="subtle" size="sm" />
      </div>
    </template>
    <div class="space-y-4">
      <ul class="divide-y divide-default">
        <li v-for="level in levels" :key="level" class="flex items-start gap-3 py-3">
          <USwitch
            :model-value="allowed.has(level)"
            :disabled="!editable || level === 'Public'"
            :aria-label="`Allow ${levelMeta[level].label}`"
            @update:model-value="(value) => toggle(level, value)"
          />
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <KeyValueLevelBadge :level="level" />
              <span class="text-xs text-muted">{{ levelMeta[level].cost }}</span>
            </div>
            <p class="mt-1 text-sm text-muted">{{ levelMeta[level].description }}</p>
            <p v-if="levelMeta[level].requirement" class="mt-0.5 text-xs text-muted">Needs: {{ levelMeta[level].requirement }}</p>
          </div>
        </li>
      </ul>

      <div class="grid gap-4 sm:grid-cols-2">
        <UFormField label="Default secret level" help="What the forms offer when a secret is created.">
          <USelect v-model="defaultLevel" :items="defaultItems" :disabled="!editable" class="w-full" />
        </UFormField>
        <UFormField label="Machines" help="Secrets reach machines only with an mTLS certificate when this is on (recommended).">
          <USwitch v-model="requireMtls" :disabled="!editable" label="Require mTLS for secret delivery" />
        </UFormField>
      </div>

      <UAlert
        v-if="inUse"
        color="warning"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        :title="`${inUse.level ?? 'The level'} is still in use`"
      >
        <template #description>
          <span>Move these keys to another level first: </span>
          <template v-for="(entry, index) in inUse.counts" :key="entry.view">
            <RouterLink
              :to="{ path: `/${context.org}/${context.project}/${entry.view}/key-values`, query: { level: inUse.level, state: 'All' } }"
              class="underline"
            >
              {{ entry.count }} in {{ entry.view }}
            </RouterLink>
            <span v-if="index < inUse.counts.length - 1">, </span>
          </template>
        </template>
      </UAlert>
      <UAlert v-if="failure" color="error" variant="subtle" icon="i-lucide-circle-alert" :description="failure" />

      <div v-if="editable" class="flex justify-end">
        <UButton label="Save secrets policy" :disabled="!dirty" :loading="saving" @click="save" />
      </div>
    </div>
  </UCard>
</template>
