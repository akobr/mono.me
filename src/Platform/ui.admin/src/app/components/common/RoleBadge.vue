<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ role?: string | null }>()

const color = computed(() => {
  switch (props.role) {
    case 'Owner':
      return 'primary'
    case 'Administrator':
      return 'info'
    case 'Contributor':
    case 'ContributorWithSecrets':
      return 'success'
    case 'Reader':
      return 'neutral'
    default:
      return 'warning'
  }
})

const label = computed(() =>
  props.role === 'ContributorWithSecrets' ? 'Contributor + secrets' : (props.role ?? 'None'),
)
</script>

<template>
  <UBadge :label="label" :color="color" variant="subtle" size="sm" />
</template>
