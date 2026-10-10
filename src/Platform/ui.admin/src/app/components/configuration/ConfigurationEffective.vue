<script setup lang="ts">
import { ref } from 'vue'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import { bindingDecorations } from '@/components/editor/setup/bindings'

/** The Effective tab: the merged document (ancestors, template, own), read-only (spec section 11.2). */
const props = defineProps<{ annotationKey: string }>()

const preferences = usePreferencesStore()
const { effective, missing } = useConfiguration(() => props.annotationKey)
const format = ref<DocumentFormat>(preferences.state.editorFormat)
const extensions = [bindingDecorations()]
</script>

<template>
  <div class="space-y-3 p-4">
    <LoadingState v-if="effective.isPending.value" />
    <EmptyState
      v-else-if="missing"
      icon="i-lucide-file-x"
      title="No configuration"
      description="Nothing is configured or inherited for this annotation yet."
    />
    <ErrorState v-else-if="effective.isError.value" :error="effective.error.value" @retry="effective.refetch()" />
    <template v-else>
      <p class="text-sm text-muted">
        Ancestors, then the type template, then the own content, merged in this order. Bindings (<code>@…</code>) are
        not evaluated here; see Resolved.
      </p>
      <DocumentEditor
        :model-value="effective.data.value?.Content ?? {}"
        :format="format"
        readonly
        :extensions="extensions"
        aria-label="Effective configuration"
        @update:format="(value) => (format = value)"
      >
        <template #actions>
          <span
            v-if="effective.data.value?.Hash"
            class="font-mono text-xs text-dimmed"
            :title="effective.data.value.Hash"
          >
            {{ effective.data.value.Hash.slice(0, 12) }}
          </span>
        </template>
      </DocumentEditor>
    </template>
  </div>
</template>
