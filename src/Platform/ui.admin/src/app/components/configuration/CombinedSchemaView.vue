<script setup lang="ts">
import { computed, ref } from 'vue'
import { isNotFound } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore, type DocumentFormat } from '@/stores/preferences'
import { metaOf } from '@/utils/annotation-key'

/** The combined schema of an annotation (`/definition`), read-only, with the applied layers in order. */
const props = defineProps<{ annotationKey: string }>()

const context = useContextStore()
const preferences = usePreferencesStore()
const { combined } = useConfiguration(() => props.annotationKey)
const format = ref<DocumentFormat>(preferences.state.editorFormat)

const base = computed(() => `/${context.org}/${context.project}/${context.view}/schemas`)

const layers = computed(() =>
  (combined.data.value?.AppliedSchemas ?? []).map((schema) => {
    const type = schema.AnnotationType ?? ''
    const kind = !schema.AnnotationKey
      ? 'Type'
      : type && metaOf(type)?.code !== metaOf(schema.AnnotationKey.slice(0, 3))?.code
        ? 'Descendant type'
        : 'Annotation'
    const to =
      kind === 'Type'
        ? `${base.value}/type/${metaOf(type)?.code ?? type}`
        : kind === 'Annotation'
          ? `${base.value}/annotation/${schema.AnnotationKey}`
          : `${base.value}/annotation/${schema.AnnotationKey}/type/${metaOf(type)?.code ?? type}`
    return {
      kind,
      label: kind === 'Type' ? (metaOf(type)?.label ?? type) : schema.AnnotationKey,
      type,
      version: schema.Version,
      to,
    }
  }),
)
</script>

<template>
  <div class="space-y-4 p-4">
    <LoadingState v-if="combined.isPending.value" />
    <EmptyState
      v-else-if="isNotFound(combined.error.value) || (!combined.isError.value && !layers.length)"
      icon="i-lucide-braces"
      title="No schema applies"
      description="Add a type, annotation or descendant-type schema to validate this configuration and get a form."
    />
    <ErrorState v-else-if="combined.isError.value" :error="combined.error.value" @retry="combined.refetch()" />
    <template v-else>
      <section class="space-y-2">
        <h3 class="text-xs font-semibold text-muted uppercase">Applied schemas, in order</h3>
        <ol class="space-y-1">
          <li v-for="(layer, index) in layers" :key="index" class="flex items-center gap-2 text-sm">
            <span class="w-4 text-xs text-dimmed">{{ index + 1 }}</span>
            <UBadge :label="layer.kind" color="neutral" variant="outline" size="sm" />
            <RouterLink :to="layer.to" class="font-mono text-xs text-primary hover:underline">{{
              layer.label
            }}</RouterLink>
            <span v-if="layer.version" class="font-mono text-xs text-dimmed">v{{ layer.version }}</span>
          </li>
        </ol>
      </section>
      <DocumentEditor
        :model-value="combined.data.value?.MergedContent ?? {}"
        :format="format"
        readonly
        aria-label="Combined schema"
        @update:format="(value) => (format = value)"
      />
    </template>
  </div>
</template>
