<script setup lang="ts">
import { hostingLink, hostingStatement, onboardingTexts as texts, subProcessors } from '@/utils/onboarding'

// Step 1 of onboarding (424-267): where customer data lives, and who else processes what. The heading is the main
// message, so it sits in a callout. The copy never says that WorkOS cannot gain access: whoever controls the identity
// provider can sign in as a user.
const statement = hostingStatement().join(' ')
</script>

<template>
  <div class="space-y-5">
    <div class="flex gap-3 rounded-lg bg-primary/10 p-4 ring ring-primary/25 ring-inset">
      <UIcon name="i-lucide-shield-check" class="mt-0.5 size-6 shrink-0 text-primary" />
      <div class="space-y-1">
        <h2 tabindex="-1" class="text-lg font-semibold text-primary outline-none">{{ texts.welcome.heading }}</h2>
        <p class="text-sm font-semibold text-highlighted">{{ texts.welcome.lead }}</p>
      </div>
    </div>

    <p class="text-sm">{{ statement }}</p>

    <div class="space-y-2">
      <h3 class="text-sm font-semibold text-highlighted">{{ texts.welcome.subProcessors }}</h3>
      <!-- A list, not a table: three columns do not fit a phone, and the purpose of WorkOS is long. -->
      <ul :aria-label="texts.welcome.subProcessors" class="divide-y divide-default rounded-md border border-default">
        <li v-for="entry in subProcessors" :key="entry.name" class="space-y-1 p-3">
          <div class="flex flex-wrap items-center gap-2">
            <span class="text-sm font-medium text-highlighted">{{ entry.name }}</span>
            <UBadge
              :label="entry.region"
              :color="entry.region === 'EU only' ? 'success' : 'neutral'"
              variant="subtle"
              size="sm"
            />
          </div>
          <p class="text-sm text-muted">{{ entry.purpose }}</p>
        </li>
      </ul>
    </div>

    <UButton
      :to="hostingLink.to"
      target="_blank"
      :label="hostingLink.label"
      trailing-icon="i-lucide-external-link"
      color="primary"
      variant="link"
      size="sm"
      class="px-0"
    />
  </div>
</template>
