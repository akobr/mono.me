<script setup lang="ts">
import { computed } from 'vue'
import { useTemplateTypes } from '@/queries/scope'
import { useCatalogStore } from '@/stores/catalog'
import { useContextStore } from '@/stores/context'
import { configurationOrigin, describeInheritance } from '@/utils/configuration-origin'

/**
 * The link from an annotation to its configuration (spec 4.2), drawn by where the configuration comes from:
 * its own stored content, only inheritance, or nothing. The data is already loaded (summaries and templates).
 */
const props = defineProps<{ annotationKey: string }>()

const catalog = useCatalogStore()
const context = useContextStore()
const canEdit = useCan('edit')
const templateTypes = useTemplateTypes()

const origin = computed(() => configurationOrigin(props.annotationKey, catalog.configurations, templateTypes.value))
const to = computed(() => `/${context.org}/${context.project}/${context.view}/configurations/${props.annotationKey}`)

const look = computed(() => {
  const value = origin.value
  switch (value.kind) {
    case 'own':
      return {
        label: `Configuration · v${value.version}`,
        variant: 'solid' as const,
        color: 'primary' as const,
        tooltip:
          value.inheritsFrom.length || value.templates.length
            ? `Own configuration, also inherits from ${describeInheritance(value)}`
            : 'Own configuration',
      }
    case 'inherited':
      return {
        label: 'Inherited configuration',
        variant: 'outline' as const,
        color: 'neutral' as const,
        tooltip: `No own values; inherits from ${describeInheritance(value)}`,
      }
    case 'none':
      return {
        label: 'No configuration',
        variant: 'ghost' as const,
        color: 'neutral' as const,
        tooltip: canEdit.value ? 'Create a configuration' : undefined,
      }
    default:
      return { label: 'Configuration', variant: 'ghost' as const, color: 'neutral' as const, tooltip: undefined }
  }
})
</script>

<template>
  <UTooltip :text="look.tooltip" :disabled="!look.tooltip">
    <UButton
      :to="to"
      size="xs"
      icon="i-lucide-settings-2"
      :label="look.label"
      :variant="look.variant"
      :color="look.color"
      :class="origin.kind === 'none' ? 'text-muted' : undefined"
      :data-origin="origin.kind"
    />
  </UTooltip>
</template>
