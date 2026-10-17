<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch, type ComponentPublicInstance } from 'vue'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { metaOf } from '@/utils/annotation-key'
import { keysWithoutConfiguration } from '@/utils/configuration-origin'

/**
 * New configuration (424-42): picks an annotation of the view without its own configuration, then hands its key to
 * the page, which opens the configuration. Nothing is created here: the Edit tab offers Create empty and Start from
 * template, as for the configuration link of an annotation.
 */
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ picked: [key: string] }>()

const catalog = useCatalogStore()
const context = useContextStore()

const key = ref<string>()
const select = useTemplateRef<{ triggerRef?: HTMLElement }>('select')
const openButton = useTemplateRef<ComponentPublicInstance>('openButton')

// A snapshot can hold keys deleted since, and a catalog still loading misses keys: the list waits for both. A failed
// load says so instead of loading forever.
const failed = computed(() => catalog.status === 'error' || !!catalog.error)
const ready = computed(
  () => catalog.status === 'ready' && !catalog.fromSnapshot && !!catalog.configurations && !failed.value,
)
const keys = computed(() =>
  ready.value ? keysWithoutConfiguration(catalog.annotations, catalog.configurations) : null,
)
// Only a key of the list opens; a key picked in another view (Back while the dialog is open) does not.
const pickable = computed(() => !!key.value && !!keys.value?.includes(key.value))
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
    ? 'Every annotation of the view has its own configuration.'
    : 'The view has no annotations yet.'
})

watch(open, (value) => {
  if (value) {
    key.value = undefined
  }
})
watch(
  () => context.scope && `${context.scope.org}/${context.scope.project}/${context.scope.view}`,
  () => (key.value = undefined),
)

// The trigger takes the focus once it can be used, also when the catalog was still loading at the start.
watch(ready, async (value) => {
  if (value && open.value) {
    await nextTick()
    select.value?.triggerRef?.focus()
  }
})

// After a pick the focus goes to Open editor, so Enter opens it; the drop-down keeps a focus placed outside of it.
async function onPick() {
  await nextTick()
  ;(openButton.value?.$el as HTMLElement | undefined)?.focus()
}

function retry() {
  if (context.scope) {
    void catalog.load(context.scope, { force: true })
  }
}

function go() {
  const picked = key.value
  if (picked && pickable.value) {
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
        <UAlert
          v-if="failed"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-alert"
          description="The catalog could not be loaded, so the annotations without a configuration are not known."
          :actions="[{ label: 'Retry', color: 'error', variant: 'outline', onClick: retry }]"
        />
        <UAlert v-else-if="nothing" color="neutral" variant="subtle" icon="i-lucide-info" :description="nothing" />
        <UFormField
          v-else
          label="Annotation"
          help="Annotations of the view without their own configuration. The one you pick opens in the editor, to start it empty or from the type template."
        >
          <USelectMenu
            ref="select"
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
            @update:model-value="onPick"
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
      <UButton ref="openButton" type="submit" form="configuration-create" label="Open editor" :disabled="!pickable" />
    </template>
  </UModal>
</template>
