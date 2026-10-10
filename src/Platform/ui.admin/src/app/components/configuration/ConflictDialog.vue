<script setup lang="ts">
import { computed } from 'vue'
import { useClipboard } from '@vueuse/core'
import { toJson } from '@/utils/json-yaml'

/**
 * A failed JSON Patch `test` (412 PatchTestFailed): someone saved the configuration since it was loaded. Shows
 * theirs (the fresh own content) against yours, with Reload, Overwrite and Copy (spec section 11.2).
 */
const open = defineModel<boolean>('open', { default: false })
const props = defineProps<{ theirs: unknown; yours: unknown; loading?: boolean }>()
const emit = defineEmits<{ reload: []; overwrite: [] }>()

const { copy, copied } = useClipboard({ legacy: true })
const theirsText = computed(() => toJson(props.theirs ?? {}))
const yoursText = computed(() => toJson(props.yours ?? {}))
</script>

<template>
  <UModal v-model:open="open" title="Someone else changed this configuration" :ui="{ content: 'max-w-5xl' }">
    <template #body>
      <p class="mb-3 text-sm text-muted">
        The stored configuration changed after you opened it. Compare their version (left) with yours (right).
      </p>
      <LoadingState v-if="loading" />
      <DiffEditor
        v-else
        :original="theirsText"
        :modified="yoursText"
        original-label="Theirs (stored)"
        modified-label="Yours"
      />
    </template>
    <template #footer>
      <div class="flex w-full flex-wrap justify-end gap-2">
        <UButton
          :label="copied ? 'Copied' : 'Copy mine'"
          icon="i-lucide-copy"
          color="neutral"
          variant="ghost"
          @click="copy(yoursText)"
        />
        <UButton label="Reload, discard mine" color="neutral" variant="outline" @click="emit('reload')" />
        <UButton label="Overwrite" color="warning" :disabled="loading" @click="emit('overwrite')" />
      </div>
    </template>
  </UModal>
</template>
