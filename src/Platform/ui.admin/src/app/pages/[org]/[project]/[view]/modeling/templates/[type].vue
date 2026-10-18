<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useContextStore } from '@/stores/context'
import { metaOf, type AnnotationTypeCode } from '@/utils/annotation-key'
import { modelingPath } from '@/utils/modeling'

const route = useRoute()
const router = useRouter()
const context = useContextStore()

const type = computed(
  () => metaOf(String((route.params as { type: string }).type))?.code as AnnotationTypeCode | undefined,
)

function close() {
  if (context.scope) {
    void router.push(modelingPath(context.scope, 'templates'))
  }
}
</script>

<template>
  <DetailPanel id="template-detail" :title="type ? `${metaOf(type)!.label} template` : 'Template'" @close="close">
    <template #header="{ close: closePanel }">
      <div class="flex items-center gap-2 border-b border-default px-4 py-3">
        <UButton
          icon="i-lucide-arrow-left"
          color="neutral"
          variant="ghost"
          size="sm"
          aria-label="Back"
          class="md:hidden"
          @click="closePanel"
        />
        <AnnotationTypeBadge v-if="type" :type="type" variant="label" />
        <h2 class="flex-1 truncate text-base font-semibold text-highlighted">Template</h2>
        <DetailPanelActions />
        <UButton
          icon="i-lucide-x"
          color="neutral"
          variant="ghost"
          size="sm"
          aria-label="Close"
          class="hidden md:inline-flex"
          @click="closePanel"
        />
      </div>
    </template>
    <TemplateDetail v-if="type" :key="type" :type="type" />
    <EmptyState v-else icon="i-lucide-circle-help" title="Unknown annotation type" />
  </DetailPanel>
</template>
