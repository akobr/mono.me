<script setup lang="ts">
import { onboardingTexts } from '@/utils/onboarding'

// The first step of onboarding (424-267): a welcome, what the platform is for, and the way to its documentation.
const texts = onboardingTexts.welcome
</script>

<template>
  <div class="space-y-4 py-4 text-center">
    <h2
      tabindex="-1"
      class="flex items-center justify-center gap-2 text-2xl font-semibold text-highlighted outline-none"
    >
      <UIcon name="i-lucide-rocket" class="size-7 shrink-0 text-primary" />
      {{ texts.heading }}
    </h2>
    <p class="mx-auto max-w-lg text-base text-muted">{{ texts.motto }}</p>
    <p class="mx-auto max-w-lg rounded-lg bg-elevated/50 p-3 text-sm">{{ texts.pricing }}</p>
    <UButton
      :to="texts.documentation.to"
      target="_blank"
      :label="texts.documentation.label"
      trailing-icon="i-lucide-external-link"
      color="primary"
      variant="link"
      size="sm"
    />
  </div>
</template>
