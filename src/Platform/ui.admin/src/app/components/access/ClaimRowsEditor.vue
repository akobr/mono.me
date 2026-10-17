<script setup lang="ts">
import { computed, useId } from 'vue'
import { claimRowError, type ClaimRow } from '@/utils/federation'

/**
 * Claim pins as rows of a claim and one accepted value (424-46): the required claims of a trusted issuer and the claims
 * of a federation rule. A claim is a top-level name (`repository_id`) or an RFC 6901 pointer to a nested one
 * (`/kubernetes.io/namespace`). Rows with the same claim are alternatives; different claims must all match. Suggested
 * claims of the issuer's platform add a row with one click.
 */
const props = withDefaults(defineProps<{ label: string; help?: string; suggestions?: string[] }>(), {
  help: undefined,
  suggestions: () => [],
})
const rows = defineModel<ClaimRow[]>({ required: true })

const id = useId()

function update(index: number, patch: Partial<ClaimRow>) {
  rows.value = rows.value.map((row, at) => (at === index ? { ...row, ...patch } : row))
}

function add(claim = '') {
  rows.value = [...rows.value, { claim, value: '' }]
}

function remove(index: number) {
  rows.value = rows.value.filter((_, at) => at !== index)
}

const unused = computed(() =>
  props.suggestions.filter((claim) => !rows.value.some((row) => row.claim.trim() === claim)),
)
</script>

<template>
  <div role="group" :aria-labelledby="`${id}-label`" class="space-y-2">
    <div>
      <p :id="`${id}-label`" class="text-sm font-medium text-default">{{ label }}</p>
      <p v-if="help" class="text-sm text-muted">{{ help }}</p>
    </div>
    <div v-for="(row, index) in rows" :key="index" class="space-y-1">
      <div class="flex items-center gap-2">
        <UInput
          :model-value="row.claim"
          class="min-w-0 flex-1"
          :ui="{ base: 'font-mono' }"
          :aria-label="`Claim ${index + 1}`"
          placeholder="Claim or /pointer"
          autocomplete="off"
          @update:model-value="(value) => update(index, { claim: String(value ?? '') })"
        />
        <span class="text-xs text-muted" aria-hidden="true">=</span>
        <UInput
          :model-value="row.value"
          class="min-w-0 flex-1"
          :ui="{ base: 'font-mono' }"
          :aria-label="`Value ${index + 1}`"
          placeholder="Accepted value"
          autocomplete="off"
          @update:model-value="(value) => update(index, { value: String(value ?? '') })"
        />
        <UButton
          icon="i-lucide-x"
          size="xs"
          color="neutral"
          variant="ghost"
          :aria-label="`Remove claim ${index + 1}`"
          @click="remove(index)"
        />
      </div>
      <p v-if="claimRowError(row)" class="text-sm text-error">{{ claimRowError(row) }}</p>
    </div>
    <div class="flex flex-wrap items-center gap-1">
      <UButton label="Add claim" icon="i-lucide-plus" size="xs" color="neutral" variant="outline" @click="add()" />
      <UButton
        v-for="claim in unused"
        :key="claim"
        :label="claim"
        :aria-label="`Add claim ${claim}`"
        icon="i-lucide-plus"
        size="xs"
        color="neutral"
        variant="ghost"
        :ui="{ label: 'font-mono' }"
        @click="add(claim)"
      />
    </div>
  </div>
</template>
