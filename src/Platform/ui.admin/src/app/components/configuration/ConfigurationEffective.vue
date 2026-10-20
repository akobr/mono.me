<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import { bindingDecorations } from '@/components/editor/setup/bindings'
import { provideBindingAssist, useBindingAssist } from '@/composables/useBindingAssist'

/**
 * The Effective tab: the merged document (ancestors, template, own), read-only (spec section 11.2), or how it is
 * composed (424-36). The choice is `?show=composition`, so a link opens the composition directly; `mode` is taken by
 * the configurations list.
 */
const props = defineProps<{ annotationKey: string }>()

const route = useRoute()
const router = useRouter()
const preferences = usePreferencesStore()
const { effective, missing } = useConfiguration(() => props.annotationKey)
const format = ref<DocumentFormat>(preferences.state.editorFormat)
// Read-only: hovering a binding explains it; the reference lists the functions and sources.
const assist = useBindingAssist({ annotationKey: () => props.annotationKey })
provideBindingAssist(assist)
const extensions = [bindingDecorations(assist)]

const views = [
  { value: 'document', label: 'Document', icon: 'i-lucide-file-json' },
  { value: 'composition', label: 'Composition', icon: 'i-lucide-list-tree' },
] as const
type EffectiveView = (typeof views)[number]['value']

const show = computed<EffectiveView>({
  get: () => (route.query.show === 'composition' ? 'composition' : 'document'),
  set: (value) => void router.replace({ query: { ...route.query, show: value === 'composition' ? value : undefined } }),
})
</script>

<template>
  <div class="space-y-3 p-4">
    <UFieldGroup size="xs" role="radiogroup" aria-label="Effective view">
      <UButton
        v-for="option in views"
        :key="option.value"
        :label="option.label"
        :icon="option.icon"
        role="radio"
        :aria-checked="show === option.value"
        :color="show === option.value ? 'primary' : 'neutral'"
        :variant="show === option.value ? 'subtle' : 'outline'"
        @click="show = option.value"
      />
    </UFieldGroup>
    <ConfigurationComposition
      v-if="show === 'composition'"
      :annotation-key="annotationKey"
      :effective-hash="effective.data.value?.Hash"
    />
    <LoadingState v-else-if="effective.isPending.value" />
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
          <BindingReference />
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
