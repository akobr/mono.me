<script setup lang="ts">
import { computed } from 'vue'
import type { Exhibition } from '@42for.net/2splatform.sdk'
import { exhibitionChoiceDescription, onboardingTexts } from '@/utils/onboarding'

/**
 * Step 3 of onboarding (424-267): the choice to join the deployment's demo project as a Reader. Nothing is sent here:
 * the page applies the choice when the wizard ends, so unchecking has nothing to undo. Without the exhibition (it did
 * not load) there is nothing to check, only Retry.
 */
const props = defineProps<{ exhibition?: Exhibition; error?: unknown; loading?: boolean }>()
defineEmits<{ retry: [] }>()
const join = defineModel<boolean>({ default: false })

const texts = onboardingTexts.exhibition
const description = computed(() => (props.exhibition ? exhibitionChoiceDescription(props.exhibition) : undefined))
</script>

<template>
  <div class="space-y-4">
    <h2 tabindex="-1" class="text-lg font-semibold text-highlighted outline-none">{{ texts.heading }}</h2>

    <LoadingState v-if="loading" />
    <ErrorState v-else-if="!exhibition" :error="error" compact @retry="$emit('retry')" />
    <template v-else>
      <p class="text-sm">{{ texts.text }}</p>
      <p v-if="exhibition.Description" class="border-s-2 border-accented ps-3 text-sm text-muted">
        {{ exhibition.Description }}
      </p>
      <UCheckbox v-model="join" :label="texts.choice" :description="description" />
      <p class="text-sm text-muted">
        {{ texts.noteStart }} <strong class="font-semibold">{{ onboardingTexts.organization.exhibitionOnly }}</strong>
        {{ texts.noteEnd }}
      </p>
    </template>
  </div>
</template>
