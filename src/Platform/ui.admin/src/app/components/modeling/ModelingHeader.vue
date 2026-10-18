<script lang="ts">
// The path of a tab opened while the tabs have the focus; the header of that page takes the focus over.
let refocus: string | undefined
</script>

<script setup lang="ts">
import { computed, nextTick, onMounted, useTemplateRef, type ComponentPublicInstance } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useGetViews } from '@42for.net/2splatform.sdk/vue-query'
import { useContextStore } from '@/stores/context'
import { hasOtherView, modelingPath, modelingTab, modelingTabs } from '@/utils/modeling'

// The header of every tab of Modeling (424-38): the navbar and the tabs, as on Monitor. Each tab is a page of its own
// (modeling/{tab}), not a ?tab= query: the schema and template details keep their Edit and Versions tabs there, and
// only a change of the path makes a dirty editor ask before it is left.
const route = useRoute()
const router = useRouter()
const context = useContextStore()

// The arguments of compare.vue and ContextSwitcher.vue, so the three share one query.
const views = useGetViews(
  () => context.org ?? '',
  () => context.project ?? '',
  undefined,
  { query: { enabled: () => !!context.project } },
)

// Compare views is off only once the views are known and none of them is another view; while they load, or after an
// error, it stays on.
const items = computed(() =>
  modelingTabs.map((tab) => ({
    ...tab,
    disabled: tab.value === 'compare' && views.isSuccess.value && !hasOtherView(views.data.value, context.view),
  })),
)

const tabs = useTemplateRef<ComponentPublicInstance>('tabs')
const tabList = () => tabs.value?.$el as HTMLElement | undefined

// A tab change opens the tab's page without the query of the previous one. When a dirty editor keeps the page, the
// route does not change and the getter keeps the current tab selected.
const tab = computed({
  get: () => modelingTab(route.path),
  set: (value: string | number) => {
    const next = modelingTabs.find((item) => item.value === value)?.value
    if (next && context.scope) {
      const target = modelingPath(context.scope, next)
      refocus = tabList()?.contains(document.activeElement) ? target : undefined
      // By the next tick the opened page has mounted its header. A navigation that did not happen (Keep editing, or
      // another tab opened meanwhile) or that kept this page (from a detail back to its list) leaves the focus alone.
      void router.push(target).finally(async () => {
        await nextTick()
        if (refocus === target) {
          refocus = undefined
        }
      })
    }
  },
})

// The opened page brings its own header, and the one that had the focus is gone with the previous page. The new
// header shows its tab where the tabs scroll (on phones), and takes the focus after a change from the tabs.
onMounted(() => {
  const selected = tabList()?.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')
  const reveal = () => selected?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  reveal()
  // on the first load the labels widen once the web font is in
  void document.fonts?.ready.then(reveal)
  if (refocus === route.path) {
    refocus = undefined
    selected?.focus()
  }
})
</script>

<template>
  <AppNavbar title="Modeling">
    <template #right>
      <slot name="right" />
    </template>
  </AppNavbar>
  <UDashboardToolbar>
    <!-- every activation is a navigation, so the arrow keys only move the focus -->
    <UTabs ref="tabs" v-model="tab" :items="items" :content="false" variant="link" size="sm" activation-mode="manual" />
  </UDashboardToolbar>
</template>
