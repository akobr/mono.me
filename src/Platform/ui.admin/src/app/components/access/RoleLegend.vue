<script setup lang="ts">
import { roleDescriptions, assignableRoles } from '@/utils/roles'
</script>

<template>
  <UPopover :content="{ align: 'end' }">
    <UButton label="Roles" icon="i-lucide-circle-help" color="neutral" variant="ghost" size="sm" />
    <template #content>
      <dl class="max-w-sm space-y-2 p-3 text-xs">
        <div v-for="role in assignableRoles" :key="role">
          <dt><RoleBadge :role="role" /></dt>
          <dd class="mt-0.5 text-muted">{{ roleDescriptions[role] }}</dd>
        </div>
      </dl>
    </template>
  </UPopover>
</template>
