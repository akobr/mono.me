<script setup lang="ts">
import { onboardingTexts, securityLinks } from '@/utils/onboarding'

// The security step of onboarding (424-267): secrets, machines, people and how the platform is tested.
const texts = onboardingTexts.security
// Literal icon names: the CSP of the admin UI blocks names built at runtime.
const sections = [
  { icon: 'i-lucide-key-round', title: texts.secrets.title, texts: [texts.secrets.text] },
  { icon: 'i-lucide-server-cog', title: texts.machines.title, texts: [texts.machines.text] },
  { icon: 'i-lucide-users', title: texts.people.title, texts: [texts.people.text, texts.people.identityProvider] },
  { icon: 'i-lucide-bug-off', title: texts.testing.title, texts: [texts.testing.text] },
]
</script>

<template>
  <div class="space-y-5">
    <div class="space-y-1">
      <h2 tabindex="-1" class="flex items-center gap-2 text-lg font-semibold text-highlighted outline-none">
        <UIcon name="i-lucide-lock-keyhole" class="size-5 shrink-0 text-primary" />
        {{ texts.heading }}
      </h2>
      <p class="text-sm">{{ texts.lead }}</p>
    </div>

    <div v-for="section in sections" :key="section.title" class="flex gap-3">
      <UIcon :name="section.icon" class="mt-0.5 size-5 shrink-0 text-primary" />
      <div class="space-y-1">
        <h3 class="text-sm font-semibold text-highlighted">{{ section.title }}</h3>
        <p v-for="text in section.texts" :key="text" class="text-sm">{{ text }}</p>
      </div>
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
