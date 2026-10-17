<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { metaOf } from '@/utils/annotation-key'
import { keysWithoutConfiguration } from '@/utils/configuration-origin'

/**
 * New configuration (424-42): picks an annotation of the view without a configuration, then hands its key to the
 * page, which opens the configuration. Nothing is created here: the Edit tab offers Create empty and Start from
 * template, as for the configuration link of an annotation.
 */
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ picked: [key: string] }>()

const catalog = useCatalogStore()
const context = useContextStore()

const key = ref<string>()

// A snapshot can hold keys deleted since, and a catalog still loading misses keys: the list waits for both.
const ready = computed(() => catalog.status === 'ready' && !catalog.fromSnapshot && !!catalog.configurations)
const keys = computed(() =>
  ready.value ? keysWithoutConfiguration(catalog.annotations, catalog.configurations) : null,
)
const items = computed(() =>
  (keys.value ?? []).map((value) => {
    const meta = metaOf(value.slice(0, 3))
    return { label: value, value, icon: meta?.icon, ui: { itemLeadingIcon: meta?.textClass } }
  }),
)
const nothing = computed(() => {
  if (!keys.value || keys.value.length > 0) {
    return undefined
  }

  return catalog.annotations.length
    ? 'Every annotation of the view has a configuration.'
    : 'The view has no annotations yet.'
})

watch(open, (value) => {
  if (value) {
    key.value = undefined
  }
})

function go() {
  const picked = key.value
  if (picked) {
    open.value = false
    emit('picked', picked)
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="New configuration"
    :description="`In ${context.org}/${context.project}, view ${context.view}.`"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <form id="configuration-create" class="space-y-4" novalidate @submit.prevent="go">
        <UAlert v-if="nothing" color="neutral" variant="subtle" icon="i-lucide-info" :description="nothing" />
        <UFormField
          v-else
          label="Annotation"
          help="Annotations of the view without a configuration. Their configuration opens next, to start it empty or from the type template."
        >
          <USelectMenu
            v-model="key"
            :items="items"
            value-key="value"
            :disabled="!ready"
            :loading="!ready"
            :placeholder="ready ? 'Pick an annotation' : 'Loading the catalog…'"
            :search-input="{ placeholder: 'Search keys…' }"
            virtualize
            autofocus
            class="w-full"
            :ui="{ value: 'font-mono' }"
          >
            <template #item-label="{ item }">
              <KeyText :value="(item as { value: string }).value" size="sm" />
            </template>
          </USelectMenu>
        </UFormField>
      </form>
    </template>
    <template #footer>
      <UButton label="Cancel" color="neutral" variant="ghost" @click="open = false" />
      <UButton type="submit" form="configuration-create" label="Open editor" :disabled="!key" />
    </template>
  </UModal>
</template>
