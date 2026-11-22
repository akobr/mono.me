<script setup lang="ts">
import { computed, ref } from 'vue'
import type { AccountRoleName } from '@42for.net/2splatform.sdk'
import { useLeaveProject } from '@/composables/useLeaveProject'
import { hasUnsavedChanges } from '@/composables/useUnsavedChanges'
import { roleLabel } from '@/utils/roles'

/**
 * "Leave project" in the Project card of Settings (424-271), for every member of the project: the API lets any member
 * leave without a recent sign-in, but the last Owner. The confirmation says what goes and how to come back.
 */
const props = defineProps<{
  projectKey: string
  role: AccountRoleName
  /** The caller is the project's only Owner: the button is disabled, and the text says why. */
  lastOwner?: boolean
  /** The project is the deployment's exhibition, which the user can join again on Organizations. */
  exhibition?: boolean
}>()

const { leaving, leave } = useLeaveProject()
const confirming = ref(false)

/**
 * What the confirmation was opened for. It stays as it was while the leave ends, when the role is gone already, and when
 * the context switcher moves on while the dialog is open: the leave is for the project of the click.
 */
const opened = ref<{ projectKey: string; role: AccountRoleName; exhibition: boolean; unsaved: boolean }>()

function open() {
  opened.value = {
    projectKey: props.projectKey,
    role: props.role,
    exhibition: !!props.exhibition,
    // the draft of the project settings, or a dialog with input: a leave ends the page and them
    unsaved: hasUnsavedChanges(),
  }
  confirming.value = true
}

const title = computed(() => `Leave ${opened.value?.projectKey ?? props.projectKey}?`)
const description = computed(() => {
  const target = opened.value
  if (!target) {
    return undefined
  }
  return [
    `You lose your ${roleLabel(target.role)} role on ${target.projectKey}.`,
    target.exhibition
      ? 'You can join the exhibition again from Organizations.'
      : 'To come back, you need a new invitation.',
    ...(target.unsaved ? ['Your unsaved changes on this page are lost.'] : []),
  ].join(' ')
})

async function confirm() {
  if (opened.value) {
    await leave(opened.value.projectKey)
  }
  confirming.value = false
}
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3">
    <p class="text-sm text-muted">
      {{
        lastOwner
          ? 'You are the only Owner of this project. Make another member an Owner before you leave.'
          : 'Leave the project to remove it from your organizations.'
      }}
    </p>
    <UButton
      label="Leave project"
      icon="i-lucide-log-out"
      color="error"
      variant="outline"
      :disabled="lastOwner"
      @click="open"
    />

    <ConfirmDialog
      v-model:open="confirming"
      :title="title"
      :description="description"
      confirm-label="Leave project"
      :loading="leaving"
      @confirm="confirm"
    />
  </div>
</template>
