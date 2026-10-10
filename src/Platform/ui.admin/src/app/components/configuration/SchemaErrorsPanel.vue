<script setup lang="ts">
import type { ValidationIssue } from '@/utils/schema-validation'

/** The 409 schema errors of a save, with the pointer of each failing value (spec section 11.2). */
defineProps<{ issues: ValidationIssue[]; canForce: boolean; forcing?: boolean }>()
const emit = defineEmits<{ force: []; dismiss: [] }>()
</script>

<template>
  <UAlert
    color="error"
    variant="subtle"
    icon="i-lucide-shield-alert"
    title="The configuration does not match its schema"
    :close="true"
    @update:open="(open) => !open && emit('dismiss')"
  >
    <template #description>
      <ul class="mt-1 space-y-0.5 text-xs">
        <li v-for="issue in issues" :key="`${issue.pointer}:${issue.message}`">
          <code class="font-mono">{{ issue.pointer || '/' }}</code> {{ issue.message }}
        </li>
      </ul>
      <div v-if="canForce" class="mt-2">
        <UButton
          label="Save anyway"
          color="error"
          variant="outline"
          size="xs"
          :loading="forcing"
          @click="emit('force')"
        />
      </div>
    </template>
  </UAlert>
</template>
