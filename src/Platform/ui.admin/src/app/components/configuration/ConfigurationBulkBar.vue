<script setup lang="ts">
import { computed, useId } from 'vue'

/**
 * Bulk actions for the selected rows of Configurations (Contributor and above): compare with another view and copy
 * to it (spec "Copy and Compare Annotations and Configurations Between Views", section 8). Both keep the selection:
 * they change no row of this view.
 */
const props = defineProps<{ keys: string[] }>()
const emit = defineEmits<{ clear: [] }>()

const { blocked, openCompare, copying } = useViewTransfer('configurations', () => props.keys)
// A disabled button takes no focus, so its tooltip is out of reach of a keyboard: the reason is also text beside them.
const blockedId = useId()

const formatter = new Intl.NumberFormat()
const count = computed(() => formatter.format(props.keys.length))
</script>

<template>
  <div
    class="flex flex-wrap items-center gap-2 border-b border-default bg-elevated/50 px-4 py-2 text-sm"
    role="toolbar"
    aria-label="Bulk actions"
  >
    <span class="font-medium">{{ count }} selected</span>
    <UTooltip :text="blocked" :disabled="!blocked">
      <UButton
        label="Compare with view"
        icon="i-lucide-git-compare"
        size="xs"
        color="neutral"
        variant="outline"
        :disabled="!!blocked"
        :aria-describedby="blocked ? blockedId : undefined"
        @click="openCompare()"
      />
    </UTooltip>
    <UTooltip :text="blocked" :disabled="!blocked">
      <UButton
        label="Copy to view"
        icon="i-lucide-copy-plus"
        size="xs"
        color="neutral"
        variant="outline"
        :disabled="!!blocked"
        :aria-describedby="blocked ? blockedId : undefined"
        @click="copying = true"
      />
    </UTooltip>
    <span v-if="blocked" :id="blockedId" class="text-xs text-muted">{{ blocked }}</span>
    <UButton class="ms-auto" label="Clear selection" size="xs" color="neutral" variant="ghost" @click="emit('clear')" />

    <ViewCopyDialog v-model:open="copying" kind="configurations" :keys="keys" />
  </div>
</template>
