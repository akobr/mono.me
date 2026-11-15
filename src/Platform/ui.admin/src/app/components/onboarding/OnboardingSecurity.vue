<script setup lang="ts">
import { onboardingTexts, securityLinks } from '@/utils/onboarding'

// Step 2 of onboarding (424-267): federated auth of machines, and how people sign in. A deployment with an identity
// provider of its own is named with its limit: the Admin UI, which shows this text, does not run in one.
const texts = onboardingTexts.security
</script>

<template>
  <div class="space-y-5">
    <div class="space-y-1">
      <h2 tabindex="-1" class="text-lg font-semibold text-highlighted outline-none">{{ texts.heading }}</h2>
      <p class="text-sm">{{ texts.lead }}</p>
    </div>

    <div class="space-y-1">
      <h3 class="text-sm font-semibold text-highlighted">{{ texts.machines.title }}</h3>
      <p class="text-sm">{{ texts.machines.text }}</p>
    </div>

    <div class="space-y-2">
      <h3 class="text-sm font-semibold text-highlighted">{{ texts.people.title }}</h3>
      <p class="text-sm">{{ texts.people.text }}</p>
      <ul class="list-disc space-y-1 ps-5 text-sm">
        <li v-for="provider in texts.people.providers" :key="provider.name">
          <strong class="font-semibold text-highlighted">{{ provider.name }}</strong
          >{{ provider.rest }}
        </li>
      </ul>
      <p class="text-sm">{{ texts.people.limit }}</p>
    </div>

    <div class="flex flex-wrap gap-x-5 gap-y-1">
      <UButton
        v-for="link in securityLinks"
        :key="link.to"
        :to="link.to"
        target="_blank"
        :label="link.label"
        trailing-icon="i-lucide-external-link"
        color="primary"
        variant="link"
        size="sm"
        class="px-0"
      />
    </div>
  </div>
</template>
