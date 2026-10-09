<script setup lang="ts">
import { computed } from 'vue'
import type { AccountRoleName } from '@42for.net/2splatform.sdk'
import { capabilityRoles, type Capability, type RoleScope } from '@/composables/useRole'

/**
 * Renders the slot when the caller's role allows the capability. With `mode="disable"` the slot renders
 * always and receives `allowed` and a tooltip text naming the required role.
 */
const props = withDefaults(
  defineProps<{ capability: Capability | AccountRoleName; scope?: RoleScope; mode?: 'hide' | 'disable' }>(),
  { scope: 'project', mode: 'hide' },
)

const role = useRole()
const required = computed(() =>
  props.capability in capabilityRoles
    ? capabilityRoles[props.capability as Capability]
    : (props.capability as AccountRoleName),
)
const allowed = computed(() => role.can(props.capability, props.scope))
const reason = computed(() => `Requires the ${required.value} role`)
</script>

<template>
  <slot v-if="allowed || mode === 'disable'" :allowed="allowed" :reason="allowed ? undefined : reason" />
</template>
