<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { isConflict, type SchemaValidationErrorDetail, type VersionRestoreResult } from '@42for.net/2splatform.sdk'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import { errorMessage, writeFailureTitle } from '@/utils/errors'
import { parseServerErrors } from '@/utils/schema-validation'
import { versionExpired, versionsQueryKey, type VersionSource } from './version-source'

/**
 * Timeline, single-version view and compare, addressed by `?v=7&from=5` (spec section 11.3). Reused by
 * configurations, templates and all three schema kinds. A Contributor restores a version that is not the current one
 * (424-221): after a confirmation its content becomes the next version; a schema check that fails offers Restore
 * anyway, as Save anyway does. A deleted document (`deleted`) keeps its versions, so one can bring it back; without
 * any versions, `emptyTitle` is shown instead of the history.
 */
const props = defineProps<{ source: VersionSource; deleted?: boolean; emptyTitle?: string }>()

const route = useRoute()
const router = useRouter()
const preferences = usePreferencesStore()
const queryClient = useQueryClient()
const confirm = useConfirm()
const toast = useToast()
const canEdit = useCan('edit')

const versions = useQuery({
  queryKey: computed(() => versionsQueryKey(props.source)),
  queryFn: ({ signal }) => props.source.list(signal),
})

const number = (value: unknown) => {
  const parsed = Number(Array.isArray(value) ? value[0] : value)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined
}
const selected = computed(() => number(route.query.v))
const from = computed(() => number(route.query.from))

const options = computed(() =>
  [...(versions.data.value ?? [])]
    .toSorted((a, b) => (b.Version ?? 0) - (a.Version ?? 0))
    .map((entry) => ({ label: `v${entry.Version}`, value: entry.Version! })),
)
const pickFrom = ref<number>()
const pickTo = ref<number>()
watch(
  [from, selected],
  ([fromValue, selectedValue]) => {
    pickFrom.value = fromValue
    pickTo.value = selectedValue
  },
  { immediate: true },
)

const format = ref<DocumentFormat>(preferences.state.editorFormat)
const single = useQuery({
  queryKey: computed(() => [...props.source.queryKey, 'version', selected.value]),
  queryFn: ({ signal }) => props.source.content(selected.value!, signal),
  enabled: computed(() => !!selected.value && !from.value),
  meta: { silent: true },
})

function go(query: { v?: number; from?: number }) {
  void router.replace({ query: { ...route.query, v: query.v, from: query.from } })
}

const empty = computed(() => !!props.emptyTitle && versions.data.value?.length === 0)
const restorable = computed(() => !!props.source.restore && canEdit.value)
const restoring = ref<number>()
/** A restore the schema check refused, with the configurations it names (409 SchemaValidationErrorResponse). */
const refused = ref<{ version: number; details: SchemaValidationErrorDetail[]; source: VersionSource }>()
const isSchema = computed(() => props.source.document === 'schema')

/** The open version may be restored: not the current one and not expired. */
const selectedRestorable = computed(() => {
  const entry = versions.data.value?.find((candidate) => candidate.Version === selected.value)
  return restorable.value && !!entry && entry.Version !== props.source.current && !versionExpired(entry)
})

function restoredToast(version: number, result: VersionRestoreResult) {
  if (result.Outcome === 'Restored') {
    toast.add({
      title: `Restored v${version} as v${result.Version ?? '?'}`,
      color: 'success',
      icon: 'i-lucide-history',
    })
    return
  }
  toast.add({
    title: 'Nothing changed',
    description:
      result.Version === version
        ? `v${version} is the current version.`
        : `The current version v${result.Version ?? '?'} already has the content of v${version}.`,
    color: 'neutral',
    icon: 'i-lucide-info',
  })
}

// The source is the one the user confirmed for, also when the page moved on meanwhile.
async function run(source: VersionSource, version: number, force: boolean) {
  if (!source.restore || restoring.value !== undefined) {
    return
  }
  restoring.value = version
  refused.value = undefined
  let result: VersionRestoreResult
  try {
    result = await source.restore(version, force)
  } catch (error) {
    if (!force && isConflict(error) && error.schemaErrors?.length) {
      refused.value = { version, details: error.schemaErrors, source }
    } else {
      toast.add({
        title: writeFailureTitle(error, `v${version} was not restored`),
        description: errorMessage(error),
        color: 'error',
      })
    }
    restoring.value = undefined
    return
  }
  restoredToast(version, result)
  try {
    // the version is restored; a failed refresh only leaves the views behind
    await (source.afterRestore?.(result) ?? queryClient.invalidateQueries({ queryKey: [...source.queryKey] }))
  } catch (error) {
    toast.add({
      title: 'The views were not refreshed',
      description: `v${version} was restored. ${errorMessage(error)}`,
      color: 'warning',
    })
  } finally {
    restoring.value = undefined
  }
}

async function restore(version: number) {
  const source = props.source
  const confirmed = await confirm({
    title: `Restore v${version}?`,
    description: `Its content is stored again as the newest version of the ${props.source.document}${
      props.deleted ? ', which brings it back' : ''
    }. Every version stays in the history.${
      source.unsavedChanges ? ' Your unsaved changes in the editor are discarded.' : ''
    }`,
    confirmLabel: `Restore v${version}`,
  })
  if (confirmed) {
    await run(source, version, false)
  }
}

async function forceRestore() {
  const target = refused.value
  if (!target) {
    return
  }
  const issues = parseServerErrors(target.details)
  const confirmed = await confirm(
    isSchema.value
      ? {
          title: 'Restore although configurations break?',
          description: `${target.details.length} configuration(s) will no longer comply with the schema. They keep working, but their next save without force fails until they are fixed.`,
          confirmLabel: 'Restore anyway',
          color: 'error',
        }
      : {
          title: 'Restore although the schema fails?',
          description: `v${target.version} will be stored with ${issues.length} schema violation(s): ${issues
            .slice(0, 3)
            .map((issue) => `${issue.pointer || '/'} ${issue.message}`)
            .join('; ')}.`,
          confirmLabel: 'Restore anyway',
          color: 'error',
        },
  )
  if (confirmed) {
    await run(target.source, target.version, true)
  }
}
</script>

<template>
  <div class="space-y-4 p-4">
    <LoadingState v-if="versions.isPending.value" />
    <ErrorState v-else-if="versions.isError.value" :error="versions.error.value" @retry="versions.refetch()" />
    <EmptyState v-else-if="empty" icon="i-lucide-history" :title="emptyTitle!" />
    <template v-else>
      <UAlert
        v-if="deleted"
        color="neutral"
        variant="subtle"
        icon="i-lucide-info"
        :title="`The ${source.document} is deleted`"
        :description="
          restorable ? 'Restore one of its versions to bring it back.' : 'Its versions stay readable until they expire.'
        "
      />
      <UAlert
        v-if="refused"
        color="error"
        variant="subtle"
        icon="i-lucide-shield-alert"
        :title="
          isSchema
            ? `Existing configurations would not comply with v${refused.version}`
            : `v${refused.version} does not match the schema`
        "
        :close="true"
        @update:open="(open) => !open && (refused = undefined)"
      >
        <template #description>
          <NonCompliantTable :details="refused.details" class="mt-2" />
          <UButton
            v-if="restorable"
            class="mt-2"
            label="Restore anyway"
            color="error"
            variant="outline"
            size="xs"
            :loading="restoring === refused.version"
            @click="forceRestore"
          />
        </template>
      </UAlert>

      <div class="flex flex-wrap items-end gap-2">
        <UFormField label="Compare">
          <USelect v-model="pickFrom" :items="options" placeholder="From" class="w-28" />
        </UFormField>
        <UFormField label="with">
          <USelect v-model="pickTo" :items="options" placeholder="To" class="w-28" />
        </UFormField>
        <UButton
          label="Compare"
          icon="i-lucide-git-compare"
          :disabled="!pickFrom || !pickTo || pickFrom === pickTo"
          @click="go({ v: pickTo, from: pickFrom })"
        />
        <UButton v-if="selected" label="Back to history" color="neutral" variant="ghost" @click="go({})" />
      </div>

      <VersionCompare v-if="selected && from" :source="source" :version="selected" :from="from" />

      <template v-else-if="selected">
        <div class="flex flex-wrap items-center gap-2">
          <p class="text-sm">
            Version <span class="font-mono">v{{ selected }}</span>
          </p>
          <UButton
            v-if="selectedRestorable"
            class="ms-auto"
            label="Restore this version"
            icon="i-lucide-history"
            size="xs"
            color="neutral"
            variant="outline"
            :loading="restoring === selected"
            :disabled="restoring !== undefined"
            @click="restore(selected)"
          />
        </div>
        <LoadingState v-if="single.isPending.value" />
        <ErrorState
          v-else-if="single.isError.value"
          :error="single.error.value"
          title="This version is not available"
          @retry="single.refetch()"
        />
        <DocumentEditor
          v-else
          :model-value="single.data.value ?? {}"
          :format="format"
          readonly
          :aria-label="`Version ${selected}`"
          @update:format="(value) => (format = value)"
        />
      </template>

      <VersionTimeline
        :versions="versions.data.value ?? []"
        :current="source.current"
        :selected="selected"
        :from="from"
        :restorable="restorable"
        :restoring="restoring"
        @view="(version) => go({ v: version })"
        @compare="(version, previous) => go({ v: version, from: previous })"
        @restore="restore"
      />
    </template>
  </div>
</template>
