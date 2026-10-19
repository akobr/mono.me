<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  getKeyValueVersion,
  getKeyValueVersionDiffCustom,
  type DiffResult,
  type KeyValueVersion,
} from '@42for.net/2splatform.sdk'
import { useKeyValue, useKeyValueVersions } from '@/queries/scope'
import { useContextStore } from '@/stores/context'
import { formatDateTime } from '@/utils/dates'
import { errorMessage } from '@/utils/errors'
import { isClientEncrypted, isSecretLevel, maskedValue } from '@/utils/key-values'

/**
 * The Versions tab of a pair (spec 29.4): the timeline with the level of each version, View (a secret only after an
 * explicit reveal, never cached) and Compare with the previous version. HYOK shows the timeline only.
 */
const props = defineProps<{ keyName: string }>()

const context = useContextStore()
const canSecrets = useCan('secrets')
const pair = useKeyValue(() => props.keyName)
const versions = useKeyValueVersions(() => props.keyName, true)

const rows = computed(() => [...(versions.data.value ?? [])].toSorted((a, b) => (b.Version ?? 0) - (a.Version ?? 0)))
const clientEncrypted = computed(() => isClientEncrypted(pair.data.value?.Protection))

const viewed = ref<{ version: number; value?: string; error?: string }>()
const compared = ref<{ version: number; from: number; diff?: DiffResult; error?: string }>()
const busy = ref<number>()

async function view(entry: KeyValueVersion) {
  if (!context.scope || entry.Version === undefined) {
    return
  }
  busy.value = entry.Version
  compared.value = undefined
  try {
    const { org, project, view: viewName } = context.scope
    const result = await getKeyValueVersion(org, project, viewName, props.keyName, entry.Version, { reveal: isSecretLevel(entry.Protection) || undefined })
    viewed.value = { version: entry.Version, value: result.Value ?? '' }
  } catch (error) {
    viewed.value = { version: entry.Version, error: errorMessage(error) }
  } finally {
    busy.value = undefined
  }
}

async function compare(entry: KeyValueVersion) {
  if (!context.scope || entry.Version === undefined) {
    return
  }
  busy.value = entry.Version
  viewed.value = undefined
  const from = entry.Version - 1
  try {
    const { org, project, view: viewName } = context.scope
    const diff = (await getKeyValueVersionDiffCustom(org, project, viewName, props.keyName, entry.Version, from)) as DiffResult
    compared.value = { version: entry.Version, from, diff }
  } catch (error) {
    compared.value = { version: entry.Version, from, error: errorMessage(error) }
  } finally {
    busy.value = undefined
  }
}

const expiry = (entry: KeyValueVersion) =>
  entry.IsCurrent ? '—' : entry.ExpirationTime ? formatDateTime(new Date(entry.ExpirationTime)) : '—'
</script>

<template>
  <div class="ph-no-capture space-y-4 p-4">
    <LoadingState v-if="versions.isPending.value" />
    <ErrorState v-else-if="versions.isError.value" :error="versions.error.value" compact @retry="versions.refetch()" />
    <EmptyState v-else-if="rows.length === 0" icon="i-lucide-history" title="No versions" />
    <template v-else>
      <UAlert
        v-if="clientEncrypted"
        color="secondary"
        variant="subtle"
        icon="i-lucide-eye-off"
        description="The values are encrypted on the client; the platform cannot show or compare them."
      />
      <div class="overflow-x-auto rounded-md border border-default">
        <table class="w-full text-sm">
          <thead class="bg-elevated/50 text-xs text-muted">
            <tr>
              <th class="px-3 py-2 text-start font-medium">Version</th>
              <th class="px-3 py-2 text-start font-medium">Level</th>
              <th class="px-3 py-2 text-start font-medium">Author</th>
              <th class="px-3 py-2 text-start font-medium">Created</th>
              <th class="px-3 py-2 text-start font-medium">Expires</th>
              <th class="px-3 py-2" />
            </tr>
          </thead>
          <tbody class="divide-y divide-default">
            <tr v-for="entry in rows" :key="entry.Version" :class="entry.IsCurrent ? 'bg-primary/4' : ''">
              <td class="px-3 py-2 font-mono text-xs">v{{ entry.Version }}<span v-if="entry.IsCurrent" class="ms-1 text-muted">(current)</span></td>
              <td class="px-3 py-2"><KeyValueLevelBadge :level="entry.Protection" /></td>
              <td class="px-3 py-2"><AuditActorLink :actor="entry.Modified?.By" subtle /></td>
              <td class="px-3 py-2">{{ entry.Modified?.At ? formatDateTime(new Date(entry.Modified.At)) : '' }}</td>
              <td class="px-3 py-2 text-muted">{{ expiry(entry) }}</td>
              <td class="px-3 py-2 text-end">
                <template v-if="!clientEncrypted && (!isSecretLevel(entry.Protection) || canSecrets)">
                  <UButton
                    :label="isSecretLevel(entry.Protection) ? 'Reveal' : 'View'"
                    :icon="isSecretLevel(entry.Protection) ? 'i-lucide-eye' : 'i-lucide-file-text'"
                    color="neutral"
                    variant="ghost"
                    size="xs"
                    :loading="busy === entry.Version"
                    @click="view(entry)"
                  />
                  <UButton
                    v-if="(entry.Version ?? 0) > 1 || true"
                    label="Compare"
                    icon="i-lucide-git-compare"
                    color="neutral"
                    variant="ghost"
                    size="xs"
                    :loading="busy === entry.Version"
                    @click="compare(entry)"
                  />
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <UCard v-if="viewed">
        <template #header>
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-semibold">Version {{ viewed.version }}</h3>
            <UButton icon="i-lucide-x" color="neutral" variant="ghost" size="xs" aria-label="Close the version" @click="viewed = undefined" />
          </div>
        </template>
        <p v-if="viewed.error" class="text-sm text-error">{{ viewed.error }}</p>
        <pre v-else class="overflow-auto whitespace-pre-wrap break-all font-mono text-sm">{{ viewed.value || maskedValue }}</pre>
      </UCard>

      <UCard v-if="compared">
        <template #header>
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-semibold">Version {{ compared.from }} → {{ compared.version }}</h3>
            <UButton icon="i-lucide-x" color="neutral" variant="ghost" size="xs" aria-label="Close the comparison" @click="compared = undefined" />
          </div>
        </template>
        <p v-if="compared.error" class="text-sm text-error">{{ compared.error }}</p>
        <p v-else-if="!compared.diff?.Hunks?.length" class="text-sm text-muted">No differences.</p>
        <HunkList v-else :hunks="compared.diff.Hunks" />
      </UCard>
    </template>
  </div>
</template>
