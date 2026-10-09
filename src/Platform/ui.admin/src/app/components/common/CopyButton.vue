<script setup lang="ts">
import { useClipboard } from '@vueuse/core'

const props = withDefaults(
  defineProps<{ value: string; label?: string; secret?: boolean; size?: 'xs' | 'sm' | 'md' }>(),
  {
    label: undefined,
    size: 'xs',
  },
)

const { copy, copied } = useClipboard({ legacy: true, copiedDuring: 1500 })
const toast = useToast()

async function onCopy() {
  await copy(props.value)

  if (props.secret) {
    toast.add({
      title: 'The clipboard contains a secret',
      description: 'Paste it where it belongs, then clear the clipboard.',
      icon: 'i-lucide-key-round',
      color: 'warning',
    })
  }
}
</script>

<template>
  <UTooltip :text="copied ? 'Copied' : 'Copy'">
    <UButton
      :icon="copied ? 'i-lucide-check' : 'i-lucide-copy'"
      :label="label"
      :aria-label="label ?? 'Copy to clipboard'"
      color="neutral"
      variant="ghost"
      :size="size"
      @click.stop="onCopy"
    />
  </UTooltip>
</template>
