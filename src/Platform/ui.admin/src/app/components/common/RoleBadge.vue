<script setup lang="ts">
import { computed } from 'vue'
import { roleLabel } from '@/utils/roles'

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

const label = computed(() => roleLabel(props.role))
</script>

<template>
  <UBadge :label="label" :color="color" variant="subtle" size="sm" />
</template>
