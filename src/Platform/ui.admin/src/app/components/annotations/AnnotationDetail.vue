<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useClipboard, useNow } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'
import type { Annotation } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { annotationStatus } from '@/utils/annotation-status'
import { errorMessage } from '@/utils/errors'
import type { TreePerspective } from '@/utils/tree-projection'

/** Header, breadcrumb and tab navigation of the annotation detail (spec section 11.1). */
const props = defineProps<{ annotationKey: string; annotation?: Annotation }>()
const emit = defineEmits<{ close: [] }>()

const route = useRoute()
const router = useRouter()
const context = useContextStore()
const actions = useAnnotationActions()
const canEdit = useCan('edit')
const toast = useToast()
const now = useNow({ interval: 60_000 })
const { copy } = useClipboard({ legacy: true })

const confirmDelete = ref(false)
const deleting = ref(false)

const base = computed(() => `/${context.org}/${context.project}/${context.view}/annotations/${props.annotationKey}`)
const perspective = computed<TreePerspective>(() =>
  route.query.by === 'responsibility' ? 'responsibility' : 'subject',
)
const status = computed(() => (props.annotation ? annotationStatus(props.annotation, now.value.getTime()) : undefined))

const tabs = computed(() => [
  { label: 'Annotation', icon: 'i-lucide-tag', to: { path: base.value, query: route.query }, exact: true },
])

const menu = computed<DropdownMenuItem[][]>(() => [
  [
    {
      label: 'Copy link',
      icon: 'i-lucide-link',
      onSelect: async () => {
        await copy(`${location.origin}${base.value}`)
        toast.add({ title: 'Link copied', icon: 'i-lucide-link' })
      },
    },
    {
      label: 'Copy key',
      icon: 'i-lucide-copy',
      onSelect: () => void copy(props.annotationKey),
    },
  ],
  ...(canEdit.value
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
  try {
    const failed = await actions.remove(props.annotationKey)
    if (failed.length) {
      throw new Error(`${props.annotationKey} could not be deleted.`)
    }
    confirmDelete.value = false
    toast.add({ title: `${props.annotationKey} deleted`, color: 'success', icon: 'i-lucide-trash-2' })
    await router.push({ path: `/${context.org}/${context.project}/${context.view}/annotations`, query: route.query })
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
        </div>
        <KeyText :value="annotationKey" copy />
      </div>
      <UDropdownMenu :items="menu" :content="{ align: 'end' }">
        <UButton icon="i-lucide-ellipsis" color="neutral" variant="ghost" size="sm" aria-label="More actions" />
      </UDropdownMenu>
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

    <div class="flex flex-wrap items-center gap-2">
      <AnnotationBreadcrumb
        :annotation-key="annotationKey"
        :perspective="perspective"
        :query="route.query"
        class="min-w-0 flex-1"
      />
      <AnnotationLineage :annotation-key="annotationKey" />
    </div>

    <UNavigationMenu :items="tabs" highlight class="-mb-3" :ui="{ link: 'text-sm' }" />

    <ConfirmDialog
      v-model:open="confirmDelete"
      :title="`Delete ${annotationKey}?`"
      description="The annotation disappears from the catalog and its descendants lose this ancestor. This cannot be undone."
      :confirm-text="annotationKey"
      :loading="deleting"
      @confirm="remove"
    />
  </div>
</template>
