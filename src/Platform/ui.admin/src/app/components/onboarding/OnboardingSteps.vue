<script setup lang="ts">
import { onboardingTexts as texts, stepLabel, stepNames, type OnboardingStep } from '@/utils/onboarding'

/**
 * The step list of the onboarding wizard, in the house pattern of a step list. UStepper is not used: it announces the
 * steps from zero and its triggers react to mousedown only. Every step is reachable at any time; below sm only the
 * current one shows its name.
 */
defineProps<{ steps: readonly OnboardingStep[]; current: OnboardingStep; disabled?: boolean }>()
defineEmits<{ select: [step: OnboardingStep] }>()
</script>

<template>
  <nav :aria-label="texts.stepList" class="@container">
    <ol class="flex flex-nowrap items-center gap-x-1 @min-[37rem]:gap-x-1.5">
      <li v-for="(step, index) in steps" :key="step">
        <button
          type="button"
          class="flex min-h-9 items-center gap-1.5 whitespace-nowrap rounded-full p-1 text-sm focus-visible:outline-2 focus-visible:outline-primary disabled:cursor-not-allowed disabled:opacity-75"
          :class="
            step === current
              ? 'pe-2.5 font-medium text-highlighted'
              : 'text-muted hover:text-highlighted @min-[37rem]:pe-2.5'
          "
          :aria-label="stepLabel(index + 1, step)"
          :aria-current="step === current ? 'step' : undefined"
          :disabled="disabled"
          @click="$emit('select', step)"
        >
          <span
            class="flex size-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold"
            :class="step === current ? 'bg-primary text-inverted' : 'bg-elevated text-default'"
          >
            {{ index + 1 }}
          </span>
          <span :class="step === current ? '' : 'sr-only @min-[37rem]:not-sr-only'">{{ stepNames[step] }}</span>
        </button>
      </li>
    </ol>
  </nav>
</template>
