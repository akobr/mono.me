<script setup lang="ts">
import { exhibitionTexts } from '@/utils/exhibition'
import { exhibitionChoiceDescription, onboardingTexts } from '@/utils/onboarding'

/**
 * The offer of the exhibition on Organizations (424-271), for a user who has an account and no role in it: the wizard's
 * words about the demo project and Join the exhibition. A button, not a link: the page lists no project for it.
 */
defineProps<{ joining?: boolean }>()
defineEmits<{ join: [] }>()
</script>

<template>
  <UCard variant="subtle" data-exhibition-offer :ui="{ body: 'space-y-2' }">
    <template #header>
      <div class="flex items-center gap-2">
        <UIcon name="i-lucide-sparkles" class="size-4 shrink-0 text-primary" />
        <h2 class="font-semibold">{{ exhibitionTexts.heading }}</h2>
      </div>
    </template>
    <p class="text-sm">{{ onboardingTexts.exhibition.text }}</p>
    <p class="text-sm text-muted">{{ exhibitionChoiceDescription() }}</p>
    <template #footer>
      <UButton :label="exhibitionTexts.join" icon="i-lucide-log-in" :loading="joining" @click="$emit('join')" />
    </template>
  </UCard>
</template>
