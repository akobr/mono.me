<script setup lang="ts">
import { computed, nextTick, ref, shallowRef, useTemplateRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getExhibition, isNotFound, type Exhibition } from '@42for.net/2splatform.sdk'
import { useOnboardingEnd } from '@/composables/useOnboardingEnd'
import type { OrganizationForm } from '@/forms/names.schema'
import { useSessionStore } from '@/stores/session'
import { errorMessage } from '@/utils/errors'
import { onboardingSteps, onboardingTexts as texts, welcomeLine, type OnboardingStep } from '@/utils/onboarding'

definePage({ meta: { layout: 'bare', allowWithoutAccount: true, wide: true } })

/**
 * The onboarding wizard of a user without an account (424-267): where the data lives, how people and machines sign
 * in, the optional exhibition, and the organization with its first project. The step and the choice of the exhibition
 * are in the query, so the browser's back and forward move between the steps and a reload keeps both.
 */
const route = useRoute()
const router = useRouter()
const session = useSessionStore()

/** The wizard waits this long for the exhibition; then step 3 offers Retry. */
const exhibitionTimeoutMs = 5_000

const exhibition = shallowRef<Exhibition>()
const exhibitionError = shallowRef<unknown>()
const exhibitionLoading = ref(false)
/** Undefined until the first answer, so the wizard shows no list of steps that it would change; false after a 404. */
const hasExhibition = ref<boolean>()

async function loadExhibition() {
  exhibitionLoading.value = true
  try {
    exhibition.value = await getExhibition({ timeoutMs: exhibitionTimeoutMs })
    exhibitionError.value = undefined
    hasExhibition.value = true
  } catch (error) {
    // Only a 404 says that the deployment has none. Anything else keeps the step, with Retry in it.
    const none = isNotFound(error)
    if (none && hasExhibition.value && current.value === 'exhibition') {
      // A Retry found that there is none after all. The step goes, and the user goes on to the one that followed it,
      // not back to the first.
      await router.replace({ query: { ...route.query, step: 'organization' } })
    }
    exhibitionError.value = none ? undefined : error
    hasExhibition.value = !none
  } finally {
    exhibitionLoading.value = false
  }
}

void loadExhibition()

// Query values arrive as strings or arrays.
const first = (value: unknown) => (Array.isArray(value) ? value[0] : value)

const steps = computed(() => onboardingSteps.filter((step) => step !== 'exhibition' || hasExhibition.value !== false))
// No step, an unknown one, or the exhibition of a deployment without one: the first step.
const current = computed<OnboardingStep>(
  () => steps.value.find((step) => step === first(route.query.step)) ?? 'welcome',
)
const previous = computed(() => steps.value[steps.value.indexOf(current.value) - 1])
const next = computed(() => steps.value[steps.value.indexOf(current.value) + 1])

/** A history entry per step; the rest of the query stays (the choice, the mock's scenario). */
function go(step: OnboardingStep | undefined) {
  if (!step) {
    return
  }
  const { step: _step, ...query } = route.query
  void router.push({ query: step === 'welcome' ? query : { ...query, step } })
}

/** The choice of step 3, in the query next to the step; it counts once the exhibition has loaded. */
const join = computed({
  get: () => first(route.query.join) === '1',
  set: (value) => {
    const { join: _join, ...query } = route.query
    void router.replace({ query: value ? { ...query, join: '1' } : query })
  },
})
const joining = computed(() => join.value && exhibition.value !== undefined)

const end = useOnboardingEnd(() => exhibition.value)
const working = computed(() => end.state.value === 'working')
const submit = (values: OrganizationForm) => end.createOrganization(values, joining.value)
const joinFailure = computed(() => `${texts.end.accountReady} ${errorMessage(end.joinError.value)}`)

// Which of the two ways to end is in flight: the form shows its own, this is the button's.
const joiningOnly = ref(false)
async function joinOnly() {
  joiningOnly.value = true
  try {
    await end.joinOnly()
  } finally {
    joiningOnly.value = false
  }
  // The button was disabled while it worked, which dropped the focus. After a failure it takes it back, to try again.
  if (end.exhibitionOnlyError.value !== undefined) {
    await nextTick()
    content.value?.querySelector<HTMLElement>('[data-exhibition-only]')?.focus()
  }
}

// After a change of step the focus goes to its heading, so a screen reader announces the step; on the last step to
// the first field of the form. The first load moves no focus: there the step has no previous one.
const content = useTemplateRef<HTMLElement>('content')
watch(
  () => (hasExhibition.value === undefined ? undefined : current.value),
  async (step, before) => {
    if (step && before) {
      // v-show shows the last step after this callback, in the same flush, and a field of a hidden step takes no focus.
      await nextTick()
      content.value
        ?.querySelector<HTMLElement>(`[data-step="${step}"] ${step === 'organization' ? 'input' : 'h2'}`)
        ?.focus()
    }
  },
  { flush: 'post' },
)

// In joinFailed the form is gone, and the focus with it: Try again takes it, so the keyboard goes on from there. It
// takes it again after a try that failed, because the button drops the focus while it works.
const failed = useTemplateRef<HTMLElement>('failed')
watch(
  () => end.state.value === 'joinFailed' && !end.retrying.value,
  async (ready) => {
    if (ready) {
      await nextTick()
      failed.value?.querySelector<HTMLElement>('[data-try-again]')?.focus()
    }
  },
  { flush: 'post' },
)
</script>

<template>
  <div class="space-y-4">
    <!-- ph-no-capture: the name is personal data. -->
    <h1 class="ph-no-capture text-center text-xl font-semibold text-highlighted">
      {{ welcomeLine(session.user?.name) }}
    </h1>

    <UCard>
      <LoadingState v-if="hasExhibition === undefined" />

      <!-- The account exists and the join failed: the steps are over, the error never reaches the form. -->
      <div v-else-if="end.state.value === 'joinFailed'" ref="failed" class="space-y-4">
        <UAlert
          role="alert"
          color="error"
          variant="subtle"
          icon="i-lucide-circle-alert"
          :title="texts.end.joinFailed"
          :description="joinFailure"
        />
        <div class="flex flex-wrap justify-end gap-2">
          <UButton
            :label="texts.end.withoutExhibition"
            color="neutral"
            variant="ghost"
            :disabled="end.retrying.value"
            @click="end.continueWithout()"
          />
          <UButton
            data-try-again
            :label="texts.end.tryAgain"
            icon="i-lucide-rotate-cw"
            :loading="end.retrying.value"
            @click="end.retryJoin()"
          />
        </div>
      </div>

      <div v-else class="space-y-6">
        <OnboardingSteps :steps="steps" :current="current" :disabled="working" @select="go" />

        <div ref="content">
          <section v-if="current === 'welcome'" data-step="welcome">
            <OnboardingWelcome />
          </section>
          <section v-else-if="current === 'security'" data-step="security">
            <OnboardingSecurity />
          </section>
          <section v-else-if="current === 'exhibition'" data-step="exhibition">
            <OnboardingExhibition
              v-model="join"
              :exhibition="exhibition"
              :error="exhibitionError"
              :loading="exhibitionLoading"
              @retry="loadExhibition"
            />
          </section>

          <!-- Stays mounted, so the values of the form survive Back and Next. -->
          <section v-show="current === 'organization'" data-step="organization" class="space-y-5">
            <div class="space-y-1">
              <h2 class="text-lg font-semibold text-highlighted">{{ texts.organization.heading }}</h2>
              <p class="text-sm text-muted">{{ texts.organization.text }}</p>
            </div>
            <OrganizationForm
              :submit="submit"
              :submit-label="texts.organization.submit"
              :autofocus="false"
              :validate-on-blur="false"
              :disabled="end.state.value !== 'idle'"
            >
              <template #actions>
                <UButton
                  :label="texts.back"
                  color="neutral"
                  variant="ghost"
                  :disabled="working"
                  @click="go(previous)"
                />
              </template>
            </OrganizationForm>

            <div v-if="joining" class="space-y-3 border-t border-default pt-4">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="text-sm">{{ texts.organization.joining }}</p>
                <UButton
                  data-exhibition-only
                  :label="texts.organization.exhibitionOnly"
                  color="neutral"
                  variant="outline"
                  :loading="joiningOnly"
                  :disabled="working"
                  @click="joinOnly"
                />
              </div>
              <UAlert
                v-if="end.exhibitionOnlyError.value !== undefined"
                role="alert"
                color="error"
                variant="subtle"
                icon="i-lucide-circle-alert"
                :description="errorMessage(end.exhibitionOnlyError.value)"
              />
            </div>
          </section>
        </div>

        <div v-if="current !== 'organization'" class="flex justify-end gap-2">
          <UButton
            v-if="previous"
            :label="texts.back"
            color="neutral"
            variant="ghost"
            :disabled="working"
            @click="go(previous)"
          />
          <UButton v-if="next" :label="texts.next" icon="i-lucide-arrow-right" trailing @click="go(next)" />
        </div>
      </div>
    </UCard>

    <div class="flex justify-center">
      <UButton :label="texts.signOut" color="neutral" variant="link" size="sm" @click="session.signOut()" />
    </div>
  </div>
</template>
