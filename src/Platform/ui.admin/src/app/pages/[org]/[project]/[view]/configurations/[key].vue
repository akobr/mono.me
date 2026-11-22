<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useClipboard } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'

// The configuration detail (spec section 11.2): Edit, Effective, Resolved, Versions and Schema as child routes.
const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
const context = useContextStore()
const canEdit = useCan('edit')
const toast = useToast()
const { copy } = useClipboard({ legacy: true })

const key = computed(() => String((route.params as { key: string }).key))
const annotation = computed(() => catalog.byKey.get(key.value))
const base = computed(() => `/${context.org}/${context.project}/${context.view}/configurations/${key.value}`)
const query = computed(() => ({ q: route.query.q }))

// More actions, as on the annotation detail: the link and the key for everyone, and "Compare with view" and "Copy to
// view" for Contributors (spec "Copy and Compare Annotations and Configurations Between Views", section 8).
const transfer = useViewTransfer('configurations', () => [key.value])
const copying = transfer.copying
// The menu item that opened the copy dialog is gone when the dialog closes: the focus goes back to the menu's button.
const more = useTemplateRef<{ $el?: HTMLElement }>('more')
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
      onSelect: () => void copy(key.value),
    },
  ],
  ...(canEdit.value ? [transfer.items.value] : []),
])

const tabs = computed(() => [
  { label: 'Edit', icon: 'i-lucide-pencil', to: { path: base.value, query: query.value }, exact: true },
  { label: 'Effective', icon: 'i-lucide-layers', to: { path: `${base.value}/effective`, query: query.value } },
  { label: 'Resolved', icon: 'i-lucide-function-square', to: { path: `${base.value}/resolved`, query: query.value } },
  { label: 'Versions', icon: 'i-lucide-history', to: { path: `${base.value}/versions`, query: query.value } },
  { label: 'Schema', icon: 'i-lucide-braces', to: { path: `${base.value}/schema`, query: query.value } },
])

function close() {
  void router.push({ path: `/${context.org}/${context.project}/${context.view}/configurations`, query: query.value })
}
</script>

<template>
  <DetailPanel id="configuration-detail" :title="key" @close="close">
    <template #header="{ close: closePanel }">
      <div class="flex flex-col gap-2 border-b border-default px-4 py-3">
        <div class="flex items-start gap-2">
          <UButton
            icon="i-lucide-arrow-left"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Back to the list"
            class="md:hidden"
            @click="closePanel"
          />
          <AnnotationTypeBadge :type="key.slice(0, 3)" variant="label" class="mt-0.5" />
          <div class="min-w-0 flex-1">
            <UTooltip text="Open the annotation">
              <UButton
                :to="`/${context.org}/${context.project}/${context.view}/annotations/${key}`"
                size="xs"
                variant="outline"
                color="neutral"
                icon="i-lucide-tags"
                label="Annotation"
                class="w-fit"
              />
            </UTooltip>
            <h2 class="truncate text-base font-semibold text-highlighted">
              {{ annotation?.Name ?? key.split('.').at(-1) }}
            </h2>
            <KeyText :value="key" copy />
          </div>
          <UDropdownMenu :items="menu" :content="{ align: 'end' }">
            <UButton
              ref="more"
              icon="i-lucide-ellipsis"
              color="neutral"
              variant="ghost"
              size="sm"
              aria-label="More actions"
            />
          </UDropdownMenu>
          <DetailPanelActions />
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Close the detail"
            class="hidden md:inline-flex"
            @click="closePanel"
          />
        </div>
        <!-- The row of the annotation detail, Lineage at the right; the screen has no perspective switch (subject). -->
        <div class="flex flex-wrap items-center gap-2">
          <AnnotationBreadcrumb
            :annotation-key="key"
            perspective="subject"
            section="configurations"
            :query="query"
            class="min-w-0 flex-1"
          />
          <AnnotationLineage :annotation-key="key" section="configurations" :query="query" />
        </div>
        <UNavigationMenu :items="tabs" highlight class="-mb-3" :ui="{ link: 'text-sm' }" />
      </div>
    </template>

    <RouterView v-slot="{ Component }">
      <component :is="Component" :annotation-key="key" />
    </RouterView>

    <ViewCopyDialog
      v-if="canEdit"
      v-model:open="copying"
      kind="configurations"
      :keys="[key]"
      @closed="more?.$el?.focus()"
    />
  </DetailPanel>
</template>
