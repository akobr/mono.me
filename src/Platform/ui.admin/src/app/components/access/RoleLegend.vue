<script setup lang="ts">
import { roleDescriptions, assignableRoles } from '@/utils/roles'
</script>

<template>
  <UPopover :content="{ align: 'end' }">
    <!-- icon-only below sm, so that Invite fits next to it without cutting the breadcrumb on a phone (424-41) -->
    <UButton
      label="Roles"
      aria-label="Roles"
      icon="i-lucide-circle-help"
      color="neutral"
      variant="ghost"
      size="sm"
      :ui="{ label: 'hidden sm:inline' }"
    />
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
