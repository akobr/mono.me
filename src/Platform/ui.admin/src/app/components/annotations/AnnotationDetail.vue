<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useClipboard, useNow } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { deletedWith } from '@/utils/annotation-key'
import { annotationStatus } from '@/utils/annotation-status'
import { actorName } from '@/utils/audit'
import { errorMessage } from '@/utils/errors'
import type { TreePerspective } from '@/utils/tree-projection'

/** Header, breadcrumb and tab navigation of the annotation detail (spec section 11.1, soft-delete spec 4.1). */
const props = withDefaults(
  defineProps<{
    annotationKey: string
    annotation?: Annotation
    /** The screen hosting the panel: tabs, breadcrumb and lineage links stay on it. */
    section?: 'annotations' | 'graph'
    /** The annotation is deleted: the panel shows its history and Restore instead of the tabs. */
    deleted?: boolean
  }>(),
  { annotation: undefined, section: 'annotations', deleted: false },
)
const emit = defineEmits<{ close: [] }>()

/** Days a deleted annotation stays restorable by default; the API does not publish its setting. */
const retentionDays = 90

const route = useRoute()
const router = useRouter()
const context = useContextStore()
const catalog = useCatalogStore()
const actions = useAnnotationActions()
const notifyDeleted = useDeletionToast()
const canEdit = useCan('edit')
const toast = useToast()
const now = useNow({ interval: 60_000 })
const { copy } = useClipboard({ legacy: true })

const confirmDelete = ref(false)
const deleting = ref(false)

// The API deletes every descendant in the inheritance graph with the annotation; the confirmation says so.
const cascade = computed(() => (confirmDelete.value ? deletedWith(props.annotationKey, catalog.byKey.keys()) : []))
const deleteDescription = computed(() => {
  const count = cascade.value.length
  return count === 0
    ? `${props.annotationKey} moves to Deleted, with its configuration. You can restore it from Deleted for ${retentionDays} days.`
    : `${props.annotationKey} and its ${count} descendant${count === 1 ? '' : 's'} move to Deleted, with their configuration. You can restore them from Deleted for ${retentionDays} days.`
})

const viewBase = computed(() => `/${context.org}/${context.project}/${context.view}`)
const base = computed(() => `${viewBase.value}/${props.section}/${props.annotationKey}`)
const perspective = computed<TreePerspective>(() =>
  route.query.by === 'responsibility' ? 'responsibility' : 'subject',
)
const status = computed(() => (props.annotation ? annotationStatus(props.annotation, now.value.getTime()) : undefined))

const tabQuery = computed(() => ({ ...route.query, cfg: undefined, v: undefined, from: undefined }))
const historyLink = computed(() => ({ path: `${base.value}/history`, query: tabQuery.value }))

const tabs = computed(() => [
  { label: 'Annotation', icon: 'i-lucide-tag', to: { path: base.value, query: tabQuery.value }, exact: true },
  { label: 'History', icon: 'i-lucide-history', to: historyLink.value },
])

// "Created 3 days ago · Updated 2 hours ago by Ada Lovelace", from the server-owned stamps.
const created = computed(() => props.annotation?.Created)
const modified = computed(() =>
  props.annotation?.Modified && props.annotation.Modified.At !== props.annotation.Created?.At
    ? props.annotation.Modified
    : undefined,
)

const menu = computed<DropdownMenuItem[][]>(() => [
  [
    props.section === 'graph'
      ? { label: 'Show in list', icon: 'i-lucide-table', to: `${viewBase.value}/annotations/${props.annotationKey}` }
      : {
          label: 'Show in graph',
          icon: 'i-lucide-waypoints',
          to: { path: `${viewBase.value}/graph/${props.annotationKey}`, query: { focus: props.annotationKey } },
        },
  ],
  [
    {
      label: 'Copy link',
      icon: 'i-lucide-link',
      onSelect: async () => {
        await copy(`${location.origin}${viewBase.value}/annotations/${props.annotationKey}`)
        toast.add({ title: 'Link copied', icon: 'i-lucide-link' })
      },
    },
    {
      label: 'Copy key',
      icon: 'i-lucide-copy',
      onSelect: () => void copy(props.annotationKey),
    },
  ],
  ...(canEdit.value && !props.deleted
    ? [
        [
          {
            label: 'Delete',
            icon: 'i-lucide-trash-2',
            color: 'error' as const,
            onSelect: () => (confirmDelete.value = true),
          },
        ],
      ]
    : []),
])

async function remove() {
  deleting.value = true
  const key = props.annotationKey
  const detailPath = `${viewBase.value}/${props.section}/${key}`
  try {
    const { failed, deletions } = await actions.remove(key)
    if (failed.length) {
      throw new Error(`${key} could not be deleted.`)
    }
    confirmDelete.value = false
    notifyDeleted(`${key} deleted`, deletions, () => void router.push({ path: detailPath, query: route.query }))
    await router.push({ path: `${viewBase.value}/${props.section}`, query: route.query })
  } catch (error) {
    toast.add({ title: 'Delete failed', description: errorMessage(error), color: 'error' })
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <div class="flex flex-col gap-2 border-b border-default px-4 py-3">
    <div class="flex items-start gap-2">
      <UButton
        icon="i-lucide-arrow-left"
        color="neutral"
        variant="ghost"
        size="sm"
        aria-label="Back to the list"
        class="md:hidden"
        @click="emit('close')"
      />
      <AnnotationTypeBadge :type="annotationKey.slice(0, 3)" variant="label" class="mt-0.5" />
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="truncate text-base font-semibold text-highlighted">
            {{ annotation?.Name ?? annotationKey.split('.').at(-1) }}
          </h2>
          <AnnotationStatusBadge v-if="status" :status="status" size="xs" />
          <UBadge v-if="deleted" label="Deleted" color="warning" variant="subtle" size="sm" icon="i-lucide-archive" />
        </div>
        <KeyText :value="annotationKey" copy />
      </div>
      <UDropdownMenu :items="menu" :content="{ align: 'end' }">
        <UButton icon="i-lucide-ellipsis" color="neutral" variant="ghost" size="sm" aria-label="More actions" />
      </UDropdownMenu>
      <DetailPanelActions />
      <UButton
        icon="i-lucide-x"
        color="neutral"
        variant="ghost"
        size="sm"
        aria-label="Close the detail"
        class="hidden md:inline-flex"
        @click="emit('close')"
      />
    </div>

    <div v-if="!deleted" class="flex flex-wrap items-center gap-x-3 gap-y-1">
      <ConfigurationLink :annotation-key="annotationKey" />
      <RouterLink
        v-if="created"
        :to="historyLink"
        class="flex flex-wrap items-center gap-1 text-xs text-muted hover:text-highlighted"
        data-testid="annotation-stamps"
      >
        <span>Created <RelativeTime :value="created.At" /></span>
        <template v-if="modified">
          <span aria-hidden="true">·</span>
          <span>Updated <RelativeTime :value="modified.At" /> by {{ actorName(modified.By) }}</span>
        </template>
      </RouterLink>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <AnnotationBreadcrumb
        :annotation-key="annotationKey"
        :perspective="perspective"
        :query="route.query"
        :section="section"
        class="min-w-0 flex-1"
      />
      <AnnotationLineage :annotation-key="annotationKey" :section="section" />
    </div>

    <UNavigationMenu v-if="!deleted" :items="tabs" highlight class="-mb-3" :ui="{ link: 'text-sm' }" />

    <ConfirmDialog
      v-model:open="confirmDelete"
      :title="`Delete ${annotationKey}?`"
      :description="deleteDescription"
      :loading="deleting"
      @confirm="remove"
    />
  </div>
</template>
