<script setup lang="ts">
import { computed } from 'vue'
import type { KeyValueProtection } from '@42for.net/2splatform.sdk'
import { levelMeta } from '@/utils/key-values'

/** The level of a pair as a badge with an icon and a text, so the colour is never the only signal (spec 29.2). */
const props = withDefaults(
  defineProps<{ level: KeyValueProtection | string | undefined; reference?: boolean; size?: 'sm' | 'md' }>(),
  { reference: false, size: 'sm' },
)

const meta = computed(() => levelMeta[(props.level ?? 'Public') as KeyValueProtection] ?? levelMeta.Public)

const color = computed(() => {
  if (props.reference) {
    return 'info' as const
  }
  switch (props.level) {
    case 'Standard':
      return 'primary' as const
    case 'Hsm':
    case 'Byok':
      return 'warning' as const
    case 'Hyok':
      return 'secondary' as const
    default:
      return 'neutral' as const
  }
})
</script>

<template>
  <UBadge
    :label="reference ? 'Reference' : meta.label"
    :icon="reference ? 'i-lucide-link' : meta.icon"
    :color="color"
    variant="subtle"
    :size="size"
    :title="reference ? 'A public pointer to a secret in your own Key Vault.' : meta.description"
  />
</template>
