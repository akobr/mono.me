<script setup lang="ts">
import { ref } from 'vue'
import { useMediaQuery } from '@vueuse/core'

/**
 * The detail beside a list (spec section 4): a resizable panel at lg and above (40 % by default, 25-70 %),
 * a right slideover at md, and a full-screen sheet with a back button below md.
 */
withDefaults(defineProps<{ id?: string; title?: string }>(), { id: 'detail', title: undefined })
const emit = defineEmits<{ close: [] }>()

const large = useMediaQuery('(min-width: 1024px)')
const medium = useMediaQuery('(min-width: 768px)')
const open = ref(true)

function close() {
  emit('close')
}
</script>

<template>
  <UDashboardPanel
    v-if="large"
    :id="id"
    resizable
    :min-size="25"
    :default-size="40"
    :max-size="70"
    :ui="{ body: 'p-0 sm:p-0 gap-0' }"
  >
    <template #header>
      <slot name="header" :close="close" />
    </template>
    <template #body>
      <slot />
    </template>
  </UDashboardPanel>

  <USlideover
    v-else
    v-model:open="open"
    side="right"
    :title="title"
    :fullscreen="!medium"
    :ui="{ content: medium ? 'max-w-[90vw] sm:max-w-[90vw]' : '', body: 'p-0 sm:p-0' }"
    @after:leave="close"
  >
    <template #content>
      <div class="flex h-full min-h-0 flex-col">
        <slot name="header" :close="() => (open = false)" />
        <div class="min-h-0 flex-1 overflow-auto">
          <slot />
        </div>
      </div>
    </template>
  </USlideover>
</template>
