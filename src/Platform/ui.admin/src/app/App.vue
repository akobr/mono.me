<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRoute } from 'vue-router'
import BareLayout from '@/layouts/BareLayout.vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useTypeAppearanceStore } from '@/stores/type-appearance'

const route = useRoute()
// Applies the type names, colors and icons of the project settings and Preferences on every page (424-13).
useTypeAppearanceStore()

// Nothing renders before the first navigation resolves, so the shell never flashes on the login page.
const ready = computed(() => route.matched.length > 0)
const layout = computed(() => (route.meta.layout === 'bare' ? BareLayout : DefaultLayout))

// Not in the e2e mode, so screenshots and Playwright runs see the app only.
const QueryDevtools =
  import.meta.env.DEV && import.meta.env.MODE !== 'e2e'
    ? defineAsyncComponent(() => import('@tanstack/vue-query-devtools').then((module) => module.VueQueryDevtools))
    : undefined
</script>

<template>
  <UApp :toaster="{ position: 'bottom-right' }" :tooltip="{ delayDuration: 300 }">
    <ApiErrorToaster />
    <component :is="layout" v-if="ready">
      <RouterView />
    </component>
    <component :is="QueryDevtools" v-if="QueryDevtools" button-position="bottom-left" />
  </UApp>
</template>
