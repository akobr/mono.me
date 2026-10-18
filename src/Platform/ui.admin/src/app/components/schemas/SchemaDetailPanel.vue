<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useContextStore } from '@/stores/context'
import { modelingPath } from '@/utils/modeling'
import { schemaApi, type SchemaTarget } from '@/utils/schema-targets'

/** The schema editor in a detail panel beside the schema list. */
const props = defineProps<{ target: SchemaTarget }>()

const router = useRouter()
const context = useContextStore()
const title = computed(() => (context.scope ? schemaApi(context.scope, props.target).title : 'Schema'))
const key = computed(() => (props.target.kind === 'type' ? undefined : props.target.key))

function close() {
  if (context.scope) {
    void router.push(modelingPath(context.scope, 'schemas'))
  }
}
</script>

<template>
  <DetailPanel id="schema-detail" :title="title" @close="close">
    <template #header="{ close: closePanel }">
      <div class="flex items-start gap-2 border-b border-default px-4 py-3">
        <UButton
          icon="i-lucide-arrow-left"
          color="neutral"
          variant="ghost"
          size="sm"
          aria-label="Back"
          class="md:hidden"
          @click="closePanel"
        />
        <UIcon name="i-lucide-braces" class="mt-1 size-4 text-muted" />
        <div class="min-w-0 flex-1">
          <h2 class="truncate text-base font-semibold text-highlighted">{{ title }}</h2>
          <RouterLink
            v-if="key"
            :to="`/${context.org}/${context.project}/${context.view}/annotations/${key}`"
            class="font-mono text-xs text-primary hover:underline"
          >
            {{ key }}
          </RouterLink>
        </div>
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
    <SchemaDetail :key="JSON.stringify(target)" :target="target" />
  </DetailPanel>
</template>
