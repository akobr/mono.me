<script setup lang="ts">
import { computed } from 'vue'
import { useCatalogStore } from '@/stores/catalog'
import { normalizeLabels } from '@/utils/labels'

/**
 * Jira-like label picker (spec section 11.1): suggestions are every label of the catalog, most used first, with
 * counts; typing filters them, and a new label can be created.
 */
const labels = defineModel<string[]>({ default: () => [] })
withDefaults(defineProps<{ disabled?: boolean; placeholder?: string }>(), {
  disabled: false,
  placeholder: 'Add labels…',
})

const catalog = useCatalogStore()
const formatter = new Intl.NumberFormat()

const items = computed(() =>
  [...catalog.labelCounts].map(([label, count]) => ({ label, value: label, suffix: formatter.format(count) })),
)
const known = computed(() => new Set(items.value.map((item) => item.value)))

function onUpdate(values: unknown) {
  labels.value = normalizeLabels((values as string[]) ?? [])
}

function onCreate(label: string) {
  labels.value = normalizeLabels([...labels.value, label])
}
</script>

<template>
  <UInputMenu
    :model-value="labels"
    :items="[...items, ...labels.filter((label) => !known.has(label)).map((label) => ({ label, value: label }))]"
    value-key="value"
    multiple
    create-item
    :disabled="disabled"
    :placeholder="placeholder"
    class="w-full"
    aria-label="Labels"
    @update:model-value="onUpdate"
    @create="onCreate"
  >
    <template #item-trailing="{ item }">
      <span v-if="(item as { suffix?: string }).suffix" class="text-xs text-dimmed">{{
        (item as { suffix?: string }).suffix
      }}</span>
    </template>
  </UInputMenu>
</template>
