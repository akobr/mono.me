<script setup lang="ts">
import { computed, useId } from 'vue'
import type { FederatedIssuer } from '@42for.net/2splatform.sdk'
import {
  issuerPlatform,
  maxRulesPerMachine,
  placeholderIn,
  ruleDraft,
  ruleHints,
  type ClaimRow,
  type RuleDraft,
} from '@/utils/federation'

/**
 * The federation rules of a Federated machine (424-46), in MachineCreateDialog and FederationRulesDialog: per rule a
 * trusted issuer of the project, the audience (the project's token endpoint, unless the issuer's platform needs
 * another), the subject and further claim pins, with hints for the issuer's platform. A machine has 1 to 10 rules; a
 * platform token must match one of them.
 */
const props = defineProps<{ issuers: FederatedIssuer[]; audience: string }>()
const rules = defineModel<RuleDraft[]>({ required: true })

const id = useId()
const byName = computed(() => new Map(props.issuers.map((issuer) => [issuer.Name ?? '', issuer])))

/** The project's issuers, and the issuer of a rule that is no longer one of them (so the select shows it). */
const issuerItems = computed(() => {
  const items = props.issuers.map((issuer) => ({
    label: issuer.Name ?? '',
    value: issuer.Name ?? '',
    description: issuer.Issuer,
  }))
  for (const rule of rules.value) {
    if (rule.issuer && !byName.value.has(rule.issuer) && !items.some((item) => item.value === rule.issuer)) {
      items.push({ label: rule.issuer, value: rule.issuer, description: 'No longer a trusted issuer' })
    }
  }
  return items
})

const issuerOf = (rule: RuleDraft) => byName.value.get(rule.issuer)
const hint = (rule: RuleDraft) => ruleHints[issuerPlatform(issuerOf(rule))]
const requiredClaims = (rule: RuleDraft) =>
  Object.entries(issuerOf(rule)?.RequiredClaims ?? {})
    .map(([claim, values]) => `${claim} = ${values.join(' or ')}`)
    .join(', ')

function update(index: number, patch: Partial<RuleDraft>) {
  rules.value = rules.value.map((rule, at) => (at === index ? { ...rule, ...patch } : rule))
}

function add() {
  const rule = ruleDraft(undefined, props.audience)
  rule.issuer = props.issuers.length === 1 ? (props.issuers[0]!.Name ?? '') : ''
  rules.value = [...rules.value, rule]
}

function remove(index: number) {
  rules.value = rules.value.filter((_, at) => at !== index)
}
</script>

<template>
  <div class="space-y-3">
    <div
      v-for="(rule, index) in rules"
      :key="index"
      role="group"
      :aria-labelledby="`${id}-rule-${index}`"
      class="space-y-3 rounded-md border border-default p-3"
    >
      <div class="flex items-center justify-between gap-2">
        <p :id="`${id}-rule-${index}`" class="text-sm font-semibold">Rule {{ index + 1 }}</p>
        <UButton
          v-if="rules.length > 1"
          icon="i-lucide-trash-2"
          size="xs"
          color="neutral"
          variant="ghost"
          :aria-label="`Remove rule ${index + 1}`"
          @click="remove(index)"
        />
      </div>
      <UFormField
        label="Trusted issuer"
        required
        :error="
          rule.issuer && !issuerOf(rule) ? `${rule.issuer} is no longer a trusted issuer of the project.` : undefined
        "
        :help="requiredClaims(rule) ? `Every token of this issuer must also carry ${requiredClaims(rule)}.` : undefined"
      >
        <USelect
          :model-value="rule.issuer || undefined"
          :items="issuerItems"
          placeholder="Pick a trusted issuer"
          class="w-full"
          @update:model-value="(value) => update(index, { issuer: String(value ?? '') })"
        />
      </UFormField>
      <UFormField
        label="Audience"
        required
        :help="
          hint(rule).audience ??
          'Request the platform token with this audience. The project’s token endpoint is unique to Storyteller; the platform’s default audience is refused.'
        "
      >
        <UInput
          :model-value="rule.audience"
          class="w-full"
          :ui="{ base: 'font-mono' }"
          autocomplete="off"
          @update:model-value="(value) => update(index, { audience: String(value ?? '') })"
        />
      </UFormField>
      <UFormField
        label="Subject"
        required
        :help="hint(rule).subject"
        :error="placeholderIn(rule.subject) ? `Replace ${placeholderIn(rule.subject)} with your value.` : undefined"
      >
        <UInput
          :model-value="rule.subject"
          class="w-full"
          :ui="{ base: 'font-mono' }"
          :placeholder="hint(rule).subjectPlaceholder"
          autocomplete="off"
          @update:model-value="(value) => update(index, { subject: String(value ?? '') })"
        />
      </UFormField>
      <ClaimRowsEditor
        :model-value="rule.claims"
        label="Claims"
        :help="hint(rule).claims ?? 'Optional pins of further claims of the token.'"
        :suggestions="hint(rule).claimSuggestions"
        @update:model-value="(claims: ClaimRow[]) => update(index, { claims })"
      />
    </div>
    <UButton
      v-if="rules.length < maxRulesPerMachine"
      label="Add rule"
      icon="i-lucide-plus"
      size="sm"
      color="neutral"
      variant="outline"
      @click="add"
    />
  </div>
</template>
