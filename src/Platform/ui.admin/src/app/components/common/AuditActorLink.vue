<script setup lang="ts">
import { computed } from 'vue'
import type { AuditActor } from '@42for.net/2splatform.sdk'
import { useContextStore } from '@/stores/context'
import { actorName } from '@/utils/audit'

/**
 * Who made a change (spec 4.3). An account links to its row on the members page for Administrators and shows its
 * id in a tooltip for everyone else; a machine links to its page for Contributors; System is plain text.
 */
const props = defineProps<{ actor?: AuditActor }>()

const context = useContextStore()
const canManageAccess = useCan('manageAccess')
const canSeeMachines = useCan('machines')

const name = computed(() => actorName(props.actor))
const to = computed(() => {
  const id = props.actor?.Id
  if (!id || !context.org || !context.project) {
    return undefined
  }
  if (props.actor?.Kind === 'Account' && canManageAccess.value) {
    return { path: `/${context.org}/${context.project}/members`, query: { highlight: id } }
  }
  if (props.actor?.Kind === 'Machine' && canSeeMachines.value) {
    return `/${context.org}/${context.project}/machines/${id}`
  }
  return undefined
})
const tooltip = computed(() =>
  props.actor?.Kind === 'Account' && props.actor.Id && props.actor.Name ? props.actor.Id : undefined,
)
</script>

<template>
  <RouterLink v-if="to" :to="to" class="font-medium text-highlighted hover:underline">{{ name }}</RouterLink>
  <UTooltip v-else-if="tooltip" :text="tooltip">
    <span class="font-medium text-highlighted">{{ name }}</span>
  </UTooltip>
  <span v-else class="font-medium text-highlighted">{{ name }}</span>
</template>
